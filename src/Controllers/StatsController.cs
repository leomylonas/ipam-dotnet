using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Returns utilisation statistics for a single subnet: total usable IPs,
/// allocated count, free count, and excluded count. The counts are computed
/// by <see cref="StatsService"/> after loading the relevant rows from the database.
///
/// Access rules mirror allocation visibility: GlobalAdmin for any subnet;
/// TenantAdmin and TenantUser for subnets accessible to their tenancy.
/// </summary>
[ApiController]
[Route("api/subnets/{subnetId:guid}/stats")]
[Authorize]
public class StatsController : IpamControllerBase
{
	/// <summary>Service that owns subnet statistics computation and access control.</summary>
	private readonly StatsService _stats;

	/// <summary>
	/// Initialises a new instance of <see cref="StatsController"/>.
	/// </summary>
	/// <param name="stats">Stats service, injected by the DI container.</param>
	public StatsController(StatsService stats)
	{
		_stats = stats;
	}

	/// <summary>
	/// Returns utilisation statistics for the specified subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to compute stats for.</param>
	/// <returns>
	/// <c>200 OK</c> with a <see cref="SubnetStatsResponse"/>;
	/// <c>400 Bad Request</c> if the subnet's stored CIDR is unparseable;
	/// <c>403 Forbidden</c> if the caller cannot access this subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpGet]
	public Task<IActionResult> Get(Guid subnetId) =>
		ExecuteAsync(async () => Ok(await _stats.GetAsync(subnetId, GetCaller())));
}
