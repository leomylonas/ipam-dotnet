using System.Net;
using System.Security.Claims;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Controllers;

/// <summary>
/// Returns utilisation statistics for a single subnet: total usable IPs,
/// allocated count, free count, and excluded count. The counts are computed
/// in-process after loading the relevant rows from the database — the subnet
/// sizes expected in this system are small enough that this is acceptable.
///
/// Access rules mirror allocation visibility: GlobalAdmin for any subnet;
/// TenantAdmin and TenantUser for subnets accessible to their tenancy.
/// </summary>
[ApiController]
[Route("api/subnets/{subnetId:guid}/stats")]
[Authorize]
public class StatsController : ControllerBase
{
	/// <summary>EF Core context for subnet, allocation, and exclusion queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="StatsController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	public StatsController(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>The role of the currently authenticated user.</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>The tenancy ID of the caller, or <c>null</c> for GlobalAdmin.</summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Returns utilisation statistics for the specified subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to compute stats for.</param>
	/// <returns>
	/// <c>200 OK</c> with a <see cref="SubnetStatsResponse"/> on success;
	/// <c>400 Bad Request</c> if the subnet's stored CIDR is unparseable (data integrity issue);
	/// <c>403 Forbidden</c> if the caller cannot access this subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpGet]
	public async Task<IActionResult> Get(Guid subnetId)
	{
		var subnet = await _db.Subnets.FindAsync(subnetId);
		if (subnet is null)
		{
			return NotFound();
		}

		// ── Access control ─────────────────────────────────────────────────────
		if (CallerRole != "GlobalAdmin")
		{
			// All non-admin callers must have a tenancy affiliation.
			if (!CallerTenancyId.HasValue)
			{
				return Forbid();
			}

			if (subnet.Type == SubnetType.Private && subnet.TenancyId != CallerTenancyId)
			{
				// Caller's tenancy does not own this private subnet.
				return Forbid();
			}

			if (subnet.Type == SubnetType.Shared)
			{
				// For shared subnets check the tenancy access table.
				// If any restriction rows exist, the caller's tenancy must be in the list.
				var hasRestrictions = await _db.SubnetTenancyAccesses
					.AnyAsync(a => a.SubnetId == subnetId);

				if (hasRestrictions &&
					!await _db.SubnetTenancyAccesses
						.AnyAsync(a => a.SubnetId == subnetId && a.TenancyId == CallerTenancyId))
				{
					return Forbid();
				}
			}
		}

		// ── IP count calculation ───────────────────────────────────────────────
		// Parse the CIDR to get the base address and prefix length for arithmetic.
		if (!IPNetwork.TryParse(subnet.Cidr, out var network))
		{
			return BadRequest("Invalid subnet CIDR");
		}

		// Convert the base address to a uint for integer arithmetic.
		// IpAllocationService.IpToUint is public static to allow reuse here.
		var baseUint = IpAllocationService.IpToUint(network.BaseAddress);

		// Compute the broadcast address by ORing the base with the host mask.
		// For a /24 the host mask is 0x000000FF (255 host bits → 8 bits → 32-24=8).
		var hostBits = 32 - network.PrefixLength;
		var broadcastUint = baseUint | ((1u << hostBits) - 1);

		// Usable IPs are those between the network address and broadcast address
		// (exclusive), i.e. base+1 through broadcast-1.
		var totalIps = (int)(broadcastUint - baseUint - 1);

		// Load allocated IPs and exclusion ranges from the database.
		var allocatedIps = await _db.Allocations
			.Where(a => a.SubnetId == subnetId)
			.Select(a => a.IpAddress)
			.ToListAsync();

		var exclusions = await _db.Exclusions
			.Where(e => e.SubnetId == subnetId)
			.ToListAsync();

		// Expand each exclusion range into a set of individual uint32 values so
		// we can count unique excluded addresses (ranges can overlap).
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

		// Count the distinct values in each bucket.
		var allocatedCount = allocatedIps.Count;
		var excludedCount = excludedSet.Count;

		// Clamp to zero in case of data inconsistencies (e.g. more allocations
		// than usable IPs due to direct DB edits or migration bugs).
		var freeCount = Math.Max(0, totalIps - allocatedCount - excludedCount);

		return Ok(new SubnetStatsResponse(subnetId, totalIps, allocatedCount, freeCount, excludedCount));
	}
}
