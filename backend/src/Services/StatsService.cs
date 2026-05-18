using System.Net;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Computes utilisation statistics for a subnet: total usable IPs, allocated
/// count, free count, and excluded count. The counts are calculated in-process
/// after loading the relevant rows because the subnets expected in this system
/// are small enough that pulling IP strings to memory is acceptable.
///
/// Access rules mirror allocation visibility: GlobalAdmin for any subnet;
/// TenantAdmin and TenantUser for subnets accessible to their tenancy.
///
/// Registered as a scoped service.
/// </summary>
public class StatsService
{
	/// <summary>EF Core context for subnet, allocation, and exclusion queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="StatsService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	public StatsService(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>
	/// Computes and returns utilisation statistics for the specified subnet.
	/// </summary>
	/// <param name="subnetId">ID of the subnet to compute stats for.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A <see cref="SubnetStatsResponse"/> with the computed counts.</returns>
	/// <exception cref="NotFoundException">Thrown if the subnet does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this subnet.</exception>
	/// <exception cref="ValidationException">Thrown if the subnet's stored CIDR cannot be parsed (data integrity issue).</exception>
	public async Task<SubnetStatsResponse> GetAsync(Guid subnetId, CallerContext caller)
	{
		var subnet = await _db.Subnets.FindAsync(subnetId)
			?? throw new NotFoundException();

		// ── Access control ─────────────────────────────────────────────────────
		await CheckSubnetAccessAsync(subnet, subnetId, caller);

		// ── IP count calculation ───────────────────────────────────────────────
		// Parse the stored CIDR to get the network topology for arithmetic.
		if (!IPNetwork.TryParse(subnet.Cidr, out var network))
		{
			throw new ValidationException("Invalid subnet CIDR");
		}

		// Convert the base address to uint for arithmetic; derive the broadcast address.
		var baseUint = IpAllocationService.IpToUint(network.BaseAddress);
		var hostBits = 32 - network.PrefixLength;
		var broadcastUint = baseUint | ((1u << hostBits) - 1);

		// Usable IPs are those between the network address and broadcast address (exclusive),
		// i.e. base+1 through broadcast-1.
		var totalIps = (int)(broadcastUint - baseUint - 1);

		// Load allocated IPs and exclusion ranges from the database.
		var allocatedIps = await _db.Allocations
			.Where(a => a.SubnetId == subnetId)
			.Select(a => a.IpAddress)
			.ToListAsync();

		var exclusions = await _db.Exclusions
			.Where(e => e.SubnetId == subnetId)
			.ToListAsync();

		// Expand each exclusion range into a set of uint32 values to count unique
		// excluded addresses accurately even when ranges overlap.
		var excludedSet = new HashSet<uint>();
		foreach (var excl in exclusions)
		{
			var start = IpAllocationService.IpToUint(IPAddress.Parse(excl.Start));
			var end = IpAllocationService.IpToUint(IPAddress.Parse(excl.End));

			for (var i = start; i <= end; i++)
			{
				excludedSet.Add(i);
			}
		}

		var allocatedCount = allocatedIps.Count;
		var excludedCount = excludedSet.Count;

		// Clamp to zero to guard against data inconsistencies (e.g. more allocations
		// than usable IPs due to direct DB edits or migration issues).
		var freeCount = Math.Max(0, totalIps - allocatedCount - excludedCount);

		return new SubnetStatsResponse(subnetId, totalIps, allocatedCount, freeCount, excludedCount);
	}

	// ── Helper ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Verifies that the caller is authorised to view stats for the given subnet.
	/// Throws <see cref="ForbiddenException"/> when access is denied.
	/// </summary>
	/// <param name="subnet">The subnet entity being accessed.</param>
	/// <param name="subnetId">The subnet's ID, used for access table lookups.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	private async Task CheckSubnetAccessAsync(Subnet subnet, Guid subnetId, CallerContext caller)
	{
		// GlobalAdmin can view stats for any subnet.
		if (caller.IsGlobalAdmin)
		{
			return;
		}

		// All non-admin callers must have a tenancy affiliation.
		if (!caller.TenancyId.HasValue)
		{
			throw new ForbiddenException();
		}

		if (subnet.Type == SubnetType.Private)
		{
			// Private subnets are scoped to their owning tenancy.
			if (subnet.TenancyId != caller.TenancyId)
			{
				throw new ForbiddenException();
			}

			return;
		}

		// For shared subnets, check the tenancy access table.
		// If any restriction rows exist the caller's tenancy must be listed.
		var hasRestrictions = await _db.SubnetTenancyAccesses
			.AnyAsync(a => a.SubnetId == subnetId);

		if (hasRestrictions &&
			!await _db.SubnetTenancyAccesses
				.AnyAsync(a => a.SubnetId == subnetId && a.TenancyId == caller.TenancyId))
		{
			throw new ForbiddenException();
		}
	}
}
