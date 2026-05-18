using System.Net;
using IpamService.Config;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IpamService.Services;

/// <summary>
/// Builds the role-specific dashboard payloads. Each of the three dashboard
/// endpoints delegates to one method here: <see cref="GetGlobalAsync"/> for
/// GlobalAdmin, <see cref="GetTenantAsync"/> for TenantAdmin, and
/// <see cref="GetUserAsync"/> for TenantUser.
///
/// All three methods batch their queries — subnets, allocation counts, and
/// exclusion ranges are loaded in at most 3–4 queries regardless of how many
/// subnets exist, keeping dashboard loads fast even at scale.
///
/// Registered as a scoped service.
/// </summary>
public class DashboardService
{
	/// <summary>EF Core context for all data access.</summary>
	private readonly AppDbContext _db;

	/// <summary>Configuration for the exhaustion threshold percentage.</summary>
	private readonly DashboardOptions _opts;

	/// <summary>
	/// Initialises a new instance of <see cref="DashboardService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="opts">Dashboard configuration options, injected by the DI container.</param>
	public DashboardService(AppDbContext db, IOptions<DashboardOptions> opts)
	{
		_db = db;
		_opts = opts.Value;
	}

	// ── GlobalAdmin ───────────────────────────────────────────────────────────

	/// <summary>
	/// Builds the GlobalAdmin dashboard payload: system-wide counts, aggregate
	/// shared-subnet utilisation, exhaustion alerts across all subnets, and the
	/// 10 most recent audit entries with resolved usernames and tenancy names.
	/// </summary>
	/// <returns>A <see cref="GlobalDashboardResponse"/> ready to serialise.</returns>
	public async Task<GlobalDashboardResponse> GetGlobalAsync()
	{
		// ── System-wide counts ────────────────────────────────────────────────
		// Three cheap scalar queries; EF batches these into a single round-trip
		// when using async over the same connection.
		var tenancyCount = await _db.Tenancies.CountAsync();
		var userCount = await _db.Users.OfType<ApplicationUser>().CountAsync();
		var sharedSubnetCount = await _db.Subnets.CountAsync(s => s.Type == SubnetType.Shared);

		// ── Subnet stats batch ────────────────────────────────────────────────
		// Load everything needed for utilisation arithmetic in 3 queries so we
		// are not issuing one query per subnet (N+1).
		var allSubnets = await _db.Subnets.ToListAsync();

		// Allocation count per subnet — GROUP BY produces one row per subnetId.
		var allocCountBySubnet = await _db.Allocations
			.GroupBy(a => a.SubnetId)
			.Select(g => new { SubnetId = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.SubnetId, x => x.Count);

		// All exclusion rows — we group in memory after the single query.
		var allExclusions = await _db.Exclusions.ToListAsync();
		var exclusionsBySubnet = allExclusions
			.GroupBy(e => e.SubnetId)
			.ToDictionary(g => g.Key, g => g.ToList());

		// ── Tenancy name lookup ───────────────────────────────────────────────
		// Needed both for the exhaustion alerts and for the audit entries.
		var tenancyNameById = await _db.Tenancies
			.ToDictionaryAsync(t => t.Id, t => t.Name);

		// ── Shared subnet aggregate utilisation ───────────────────────────────
		var sharedSubnets = allSubnets.Where(s => s.Type == SubnetType.Shared).ToList();
		var sharedUtilisation = AggregateUtilisation(sharedSubnets, allocCountBySubnet, exclusionsBySubnet);

		// ── Exhaustion alerts (all subnets) ───────────────────────────────────
		// Walk every subnet and flag those at or above the threshold.
		var exhaustionAlerts = new List<GlobalExhaustionAlert>();
		foreach (var subnet in allSubnets)
		{
			// Per-subnet utilisation uses the same helper as the aggregate calc.
			var (totalIps, allocatedIps, excludedIps) = ComputeSubnetStats(
				subnet, allocCountBySubnet, exclusionsBySubnet);

			if (totalIps <= 0)
			{
				// Skip subnets where the CIDR could not be parsed (data inconsistency guard).
				continue;
			}

			var utilisationPct = (double)allocatedIps / totalIps * 100.0;

			if (utilisationPct >= _opts.ExhaustionThresholdPercent)
			{
				// Resolve the tenancy name; shared subnets have no owning tenancy.
				var tenancyName = subnet.TenancyId.HasValue
					? tenancyNameById.GetValueOrDefault(subnet.TenancyId.Value)
					: null;

				exhaustionAlerts.Add(new GlobalExhaustionAlert(
					subnet.Id,
					subnet.Cidr,
					subnet.TenancyId,
					tenancyName,
					Math.Round(utilisationPct, 2)));
			}
		}

		// Sort descending so the most at-risk subnets appear first.
		exhaustionAlerts.Sort((a, b) => b.UtilisationPercent.CompareTo(a.UtilisationPercent));

		// ── Recent audit entries ──────────────────────────────────────────────
		// Load the 10 most recent entries, then resolve usernames in one extra query.
		var recentAudit = await _db.AuditLogs
			.OrderByDescending(a => a.Timestamp)
			.Take(10)
			.ToListAsync();

		var recentAuditDtos = await ResolveAuditEntriesAsync(recentAudit, tenancyNameById);

		return new GlobalDashboardResponse(
			tenancyCount,
			userCount,
			sharedSubnetCount,
			sharedUtilisation,
			exhaustionAlerts,
			recentAuditDtos.Select(e => new GlobalDashboardAuditEntry(
				e.Id, e.Timestamp, e.Action, e.PerformedBy, e.TenancyName, e.Detail)).ToList());
	}

	// ── TenantAdmin ───────────────────────────────────────────────────────────

	/// <summary>
	/// Builds the TenantAdmin dashboard payload: counts and utilisation stats
	/// scoped to the caller's tenancy, exhaustion alerts for all accessible
	/// subnets, and the 10 most recent tenancy-scoped audit entries.
	/// </summary>
	/// <param name="caller">The context of the authenticated TenantAdmin caller.</param>
	/// <returns>A <see cref="TenantDashboardResponse"/> ready to serialise.</returns>
	/// <exception cref="NotFoundException">
	/// Thrown if the caller's tenancy no longer exists (data integrity guard).
	/// </exception>
	public async Task<TenantDashboardResponse> GetTenantAsync(CallerContext caller)
	{
		// The caller must have a tenancy affiliation — enforced at the controller level,
		// but we guard here for clarity.
		var tenancyId = caller.TenancyId ?? throw new ForbiddenException();

		var tenancy = await _db.Tenancies.FindAsync(tenancyId)
			?? throw new NotFoundException("Tenancy not found");

		// ── Tenancy-scoped counts ─────────────────────────────────────────────
		var userCount = await _db.Users
			.OfType<ApplicationUser>()
			.CountAsync(u => u.TenancyId == tenancyId);

		// ── Private subnet stats ──────────────────────────────────────────────
		var privateSubnets = await _db.Subnets
			.Where(s => s.TenancyId == tenancyId && s.Type == SubnetType.Private)
			.ToListAsync();

		var privateSubnetIds = privateSubnets.Select(s => s.Id).ToList();

		var allocCountBySubnet = await _db.Allocations
			.Where(a => privateSubnetIds.Contains(a.SubnetId))
			.GroupBy(a => a.SubnetId)
			.Select(g => new { SubnetId = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.SubnetId, x => x.Count);

		var exclusionsBySubnet = (await _db.Exclusions
			.Where(e => privateSubnetIds.Contains(e.SubnetId))
			.ToListAsync())
			.GroupBy(e => e.SubnetId)
			.ToDictionary(g => g.Key, g => g.ToList());

		var privateUtilisation = AggregateUtilisation(privateSubnets, allocCountBySubnet, exclusionsBySubnet);

		// ── Accessible shared subnet count ────────────────────────────────────
		// A shared subnet is accessible to this tenancy if it either has no
		// tenancy restrictions at all, or explicitly grants this tenancy access.
		var accessibleSharedCount = await _db.Subnets
			.CountAsync(s => s.Type == SubnetType.Shared &&
				(!_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id) ||
				 _db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id && a.TenancyId == tenancyId)));

		// ── Exhaustion alerts (private + accessible shared) ───────────────────
		// Load accessible shared subnets separately; we need their IDs for the
		// allocation and exclusion lookups.
		var accessibleSharedSubnets = await _db.Subnets
			.Where(s => s.Type == SubnetType.Shared &&
				(!_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id) ||
				 _db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id && a.TenancyId == tenancyId)))
			.ToListAsync();

		var sharedSubnetIds = accessibleSharedSubnets.Select(s => s.Id).ToList();

		// Batch load allocation counts and exclusions for shared subnets.
		var sharedAllocCounts = await _db.Allocations
			.Where(a => sharedSubnetIds.Contains(a.SubnetId))
			.GroupBy(a => a.SubnetId)
			.Select(g => new { SubnetId = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.SubnetId, x => x.Count);

		var sharedExclusions = (await _db.Exclusions
			.Where(e => sharedSubnetIds.Contains(e.SubnetId))
			.ToListAsync())
			.GroupBy(e => e.SubnetId)
			.ToDictionary(g => g.Key, g => g.ToList());

		// Merge the private and shared lookup dictionaries for the alert pass.
		var combinedAllocCounts = new Dictionary<Guid, int>(allocCountBySubnet);
		foreach (var kv in sharedAllocCounts)
		{
			combinedAllocCounts[kv.Key] = kv.Value;
		}

		var combinedExclusions = new Dictionary<Guid, List<Exclusion>>(exclusionsBySubnet);
		foreach (var kv in sharedExclusions)
		{
			combinedExclusions[kv.Key] = kv.Value;
		}

		var allAccessibleSubnets = privateSubnets.Concat(accessibleSharedSubnets).ToList();
		var exhaustionAlerts = new List<TenantExhaustionAlert>();
		foreach (var subnet in allAccessibleSubnets)
		{
			var (totalIps, allocatedIps, _) = ComputeSubnetStats(
				subnet, combinedAllocCounts, combinedExclusions);

			if (totalIps <= 0)
			{
				continue;
			}

			var utilisationPct = (double)allocatedIps / totalIps * 100.0;

			if (utilisationPct >= _opts.ExhaustionThresholdPercent)
			{
				exhaustionAlerts.Add(new TenantExhaustionAlert(
					subnet.Id, subnet.Cidr, Math.Round(utilisationPct, 2)));
			}
		}

		exhaustionAlerts.Sort((a, b) => b.UtilisationPercent.CompareTo(a.UtilisationPercent));

		// ── Recent audit entries ──────────────────────────────────────────────
		var recentAudit = await _db.AuditLogs
			.Where(a => a.TenancyId == tenancyId)
			.OrderByDescending(a => a.Timestamp)
			.Take(10)
			.ToListAsync();

		// Resolve usernames — no tenancy name needed since the view is tenancy-scoped.
		var userIds = recentAudit.Select(a => a.UserId).Distinct().ToList();
		var usernameById = await _db.Users
			.OfType<ApplicationUser>()
			.Where(u => userIds.Contains(u.Id))
			.ToDictionaryAsync(u => u.Id, u => u.UserName!);

		var recentAuditDtos = recentAudit.Select(a => new TenantDashboardAuditEntry(
			a.Id,
			a.Timestamp,
			a.Action,
			usernameById.GetValueOrDefault(a.UserId, a.UserId),
			a.Notes)).ToList();

		return new TenantDashboardResponse(
			tenancyId,
			tenancy.Name,
			userCount,
			privateSubnets.Count,
			privateUtilisation,
			accessibleSharedCount,
			exhaustionAlerts,
			recentAuditDtos);
	}

	// ── TenantUser ────────────────────────────────────────────────────────────

	/// <summary>
	/// Builds the TenantUser dashboard payload: the caller's tenancy's recent
	/// allocations and the accessible subnets with their free IP counts.
	/// </summary>
	/// <param name="caller">The context of the authenticated TenantUser caller.</param>
	/// <returns>A <see cref="UserDashboardResponse"/> ready to serialise.</returns>
	public async Task<UserDashboardResponse> GetUserAsync(CallerContext caller)
	{
		var tenancyId = caller.TenancyId ?? throw new ForbiddenException();

		// ── Recent allocations ────────────────────────────────────────────────
		// Fetch the most recent 20 allocations for the tenancy and enrich each
		// with the subnet CIDR and the full tag set.
		var recentAllocations = await _db.Allocations
			.Where(a => a.TenancyId == tenancyId)
			.OrderByDescending(a => a.AllocatedAt)
			.Take(20)
			.ToListAsync();

		var subnetIds = recentAllocations.Select(a => a.SubnetId).Distinct().ToList();
		var subnetCidrById = await _db.Subnets
			.Where(s => subnetIds.Contains(s.Id))
			.ToDictionaryAsync(s => s.Id, s => s.Cidr);

		var allocIds = recentAllocations.Select(a => a.Id).ToList();
		var tagsByAlloc = (await _db.AllocationTags
			.Where(t => allocIds.Contains(t.AllocationId))
			.ToListAsync())
			.GroupBy(t => t.AllocationId)
			.ToDictionary(
				g => g.Key,
				g => g.ToDictionary(t => t.Key, t => t.Value));

		var recentAllocationDtos = recentAllocations.Select(a => new RecentAllocationDto(
			a.Id,
			a.IpAddress,
			subnetCidrById.GetValueOrDefault(a.SubnetId, string.Empty),
			a.AllocatedAt,
			tagsByAlloc.TryGetValue(a.Id, out var tags) ? tags : new Dictionary<string, string>()
		)).ToList();

		// ── Accessible subnets with free IP counts ────────────────────────────
		// Accessible = private subnets owned by this tenancy, plus shared subnets
		// that are either open to all or explicitly granted to this tenancy.
		var accessibleSubnets = await _db.Subnets
			.Where(s =>
				(s.Type == SubnetType.Private && s.TenancyId == tenancyId) ||
				(s.Type == SubnetType.Shared &&
					(!_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id) ||
					 _db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id && a.TenancyId == tenancyId))))
			.ToListAsync();

		var accessibleIds = accessibleSubnets.Select(s => s.Id).ToList();

		var accessibleAllocCounts = await _db.Allocations
			.Where(a => accessibleIds.Contains(a.SubnetId))
			.GroupBy(a => a.SubnetId)
			.Select(g => new { SubnetId = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.SubnetId, x => x.Count);

		var accessibleExclusions = (await _db.Exclusions
			.Where(e => accessibleIds.Contains(e.SubnetId))
			.ToListAsync())
			.GroupBy(e => e.SubnetId)
			.ToDictionary(g => g.Key, g => g.ToList());

		var accessibleSubnetDtos = accessibleSubnets.Select(s =>
		{
			var (totalIps, allocatedIps, excludedIps) = ComputeSubnetStats(
				s, accessibleAllocCounts, accessibleExclusions);
			var freeIps = Math.Max(0, totalIps - allocatedIps - excludedIps);
			return new AccessibleSubnetDto(s.Id, s.Cidr, freeIps);
		}).ToList();

		return new UserDashboardResponse(recentAllocationDtos, accessibleSubnetDtos);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Computes (totalIps, allocatedIps, excludedIps) for a single subnet using
	/// pre-loaded dictionaries rather than per-subnet queries. Returns (0, 0, 0)
	/// if the subnet's CIDR cannot be parsed.
	/// </summary>
	/// <param name="subnet">The subnet entity to compute stats for.</param>
	/// <param name="allocCountBySubnet">Pre-loaded allocation counts keyed by subnet ID.</param>
	/// <param name="exclusionsBySubnet">Pre-loaded exclusion lists keyed by subnet ID.</param>
	/// <returns>A tuple of (totalIps, allocatedIps, excludedIps).</returns>
	private static (int totalIps, int allocatedIps, int excludedIps) ComputeSubnetStats(
		Subnet subnet,
		Dictionary<Guid, int> allocCountBySubnet,
		Dictionary<Guid, List<Exclusion>> exclusionsBySubnet)
	{
		// Parse the CIDR; return a sentinel (0,0,0) for unparseable data rather
		// than throwing so callers can skip corrupt rows gracefully.
		if (!IPNetwork.TryParse(subnet.Cidr, out var network))
		{
			return (0, 0, 0);
		}

		// Derive the broadcast address from the prefix length.
		var baseUint = IpAllocationService.IpToUint(network.BaseAddress);
		var hostBits = 32 - network.PrefixLength;
		var broadcastUint = baseUint | ((1u << hostBits) - 1);

		// Usable IPs are base+1 through broadcast-1 (network and broadcast excluded).
		var totalIps = (int)(broadcastUint - baseUint - 1);

		var allocatedIps = allocCountBySubnet.TryGetValue(subnet.Id, out var cnt) ? cnt : 0;

		// Expand each exclusion range into a set of uint32 values for accurate
		// unique-address counting even when ranges overlap each other.
		var excludedSet = new HashSet<uint>();
		if (exclusionsBySubnet.TryGetValue(subnet.Id, out var exclusions))
		{
			foreach (var excl in exclusions)
			{
				if (!IPAddress.TryParse(excl.Start, out var startIp) ||
					!IPAddress.TryParse(excl.End, out var endIp))
				{
					// Skip malformed exclusion rows rather than crashing the dashboard.
					continue;
				}

				var startUint = IpAllocationService.IpToUint(startIp);
				var endUint = IpAllocationService.IpToUint(endIp);
				for (var i = startUint; i <= endUint; i++)
				{
					excludedSet.Add(i);
				}
			}
		}

		return (totalIps, allocatedIps, excludedSet.Count);
	}

	/// <summary>
	/// Aggregates per-subnet stats into a single <see cref="SubnetUtilisationDto"/>
	/// by summing totalIps, allocatedIps, and excludedIps across all supplied subnets.
	/// </summary>
	/// <param name="subnets">The subnets to aggregate.</param>
	/// <param name="allocCountBySubnet">Pre-loaded allocation counts keyed by subnet ID.</param>
	/// <param name="exclusionsBySubnet">Pre-loaded exclusion lists keyed by subnet ID.</param>
	/// <returns>A <see cref="SubnetUtilisationDto"/> with summed counts and an overall utilisation percentage.</returns>
	private static SubnetUtilisationDto AggregateUtilisation(
		IEnumerable<Subnet> subnets,
		Dictionary<Guid, int> allocCountBySubnet,
		Dictionary<Guid, List<Exclusion>> exclusionsBySubnet)
	{
		var totalIps = 0;
		var allocatedIps = 0;
		var excludedIps = 0;

		foreach (var subnet in subnets)
		{
			var (t, a, e) = ComputeSubnetStats(subnet, allocCountBySubnet, exclusionsBySubnet);
			totalIps += t;
			allocatedIps += a;
			excludedIps += e;
		}

		var freeIps = Math.Max(0, totalIps - allocatedIps - excludedIps);

		// Guard against division by zero when there are no subnets.
		var utilisationPct = totalIps > 0
			? Math.Round((double)allocatedIps / totalIps * 100.0, 2)
			: 0.0;

		return new SubnetUtilisationDto(totalIps, allocatedIps, freeIps, excludedIps, utilisationPct);
	}

	/// <summary>
	/// Helper record used internally to carry resolved audit entry data before
	/// it is projected into the role-specific DTO type.
	/// </summary>
	/// <param name="Id">Audit entry ID.</param>
	/// <param name="Timestamp">UTC timestamp.</param>
	/// <param name="Action">Action verb.</param>
	/// <param name="PerformedBy">Resolved username.</param>
	/// <param name="TenancyName">Resolved tenancy name, or null.</param>
	/// <param name="Detail">Notes field.</param>
	private record ResolvedAuditEntry(
		Guid Id,
		DateTime Timestamp,
		string Action,
		string PerformedBy,
		string? TenancyName,
		string? Detail
	);

	/// <summary>
	/// Resolves usernames and tenancy names for a list of audit log entries using
	/// pre-loaded or fetched lookup data, minimising the total query count.
	/// </summary>
	/// <param name="entries">The raw <see cref="AuditLog"/> rows to resolve.</param>
	/// <param name="tenancyNameById">Pre-loaded tenancy name dictionary keyed by tenancy ID.</param>
	/// <returns>A list of <see cref="ResolvedAuditEntry"/> records with names resolved.</returns>
	private async Task<List<ResolvedAuditEntry>> ResolveAuditEntriesAsync(
		List<AuditLog> entries,
		Dictionary<Guid, string> tenancyNameById)
	{
		if (entries.Count == 0)
		{
			return [];
		}

		// Load only the usernames for the user IDs present in these entries,
		// rather than querying the whole user table.
		var userIds = entries.Select(a => a.UserId).Distinct().ToList();
		var usernameById = await _db.Users
			.OfType<ApplicationUser>()
			.Where(u => userIds.Contains(u.Id))
			.ToDictionaryAsync(u => u.Id, u => u.UserName!);

		return entries.Select(a => new ResolvedAuditEntry(
			a.Id,
			a.Timestamp,
			a.Action,
			usernameById.GetValueOrDefault(a.UserId, a.UserId),
			a.TenancyId.HasValue
				? tenancyNameById.GetValueOrDefault(a.TenancyId.Value)
				: null,
			a.Notes)).ToList();
	}
}
