using IpamService.Models;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Provides role-specific dashboard data for the React UI. Each endpoint is
/// gated to a single role so that the UI only needs to call the one endpoint
/// appropriate for the authenticated user's role.
///
/// <list type="bullet">
///   <item><term>GET /dashboard/global</term><description>System-wide stats for GlobalAdmin.</description></item>
///   <item><term>GET /dashboard/tenant</term><description>Tenancy-scoped stats for TenantAdmin.</description></item>
///   <item><term>GET /dashboard/user</term><description>Accessible subnets and recent allocations for TenantUser.</description></item>
/// </list>
///
/// All business logic and data access is delegated to <see cref="DashboardService"/>.
/// </summary>
[ApiController]
[Route("dashboard")]
[Authorize]
public class DashboardController : IpamControllerBase
{
	/// <summary>Service that owns all dashboard query and aggregation logic.</summary>
	private readonly DashboardService _dashboard;

	/// <summary>
	/// Initialises a new instance of <see cref="DashboardController"/>.
	/// </summary>
	/// <param name="dashboard">Dashboard service, injected by the DI container.</param>
	public DashboardController(DashboardService dashboard)
	{
		_dashboard = dashboard;
	}

	/// <summary>
	/// Returns system-wide statistics for the GlobalAdmin dashboard: tenancy and
	/// user counts, aggregate shared-subnet utilisation, exhaustion alerts across
	/// all subnets, and the 10 most recent audit entries.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with a <c>GlobalDashboardResponse</c>;
	/// <c>403 Forbidden</c> if the caller is not a GlobalAdmin.
	/// </returns>
	[HttpGet("global")]
	[Authorize(Roles = Roles.GlobalAdmin)]
	public Task<IActionResult> Global() =>
		ExecuteAsync(async () => Ok(await _dashboard.GetGlobalAsync()));

	/// <summary>
	/// Returns statistics scoped to the TenantAdmin's own tenancy: user count,
	/// private subnet utilisation, accessible shared subnet count, exhaustion
	/// alerts, and the 10 most recent tenancy audit entries.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with a <c>TenantDashboardResponse</c>;
	/// <c>403 Forbidden</c> if the caller is not a TenantAdmin;
	/// <c>404 Not Found</c> if the caller's tenancy no longer exists.
	/// </returns>
	[HttpGet("tenant")]
	[Authorize(Roles = Roles.TenantAdmin)]
	public Task<IActionResult> Tenant() =>
		ExecuteAsync(async () => Ok(await _dashboard.GetTenantAsync(GetCaller())));

	/// <summary>
	/// Returns the TenantUser dashboard: accessible subnets with free IP counts
	/// and recent allocations made by the caller's tenancy.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with a <c>UserDashboardResponse</c>;
	/// <c>403 Forbidden</c> if the caller is not a TenantUser.
	/// </returns>
	[HttpGet("user")]
	[Authorize(Roles = Roles.TenantUser)]
	public Task<IActionResult> UserDashboard() =>
		ExecuteAsync(async () => Ok(await _dashboard.GetUserAsync(GetCaller())));
}
