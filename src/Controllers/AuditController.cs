using System.Security.Claims;
using IpamService.Data;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Controllers;

/// <summary>
/// Provides read-only access to the audit log. The audit log is an append-only
/// record of significant system events (allocations, releases, subnet changes,
/// user management) written automatically by services and controllers. It cannot
/// be modified via the API.
///
/// Access rules:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Sees all audit entries across all tenancies.</description></item>
///   <item><term>TenantAdmin</term><description>Sees only audit entries belonging to their own tenancy.</description></item>
///   <item><term>TenantUser</term><description>Cannot access the audit log.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController : ControllerBase
{
	/// <summary>EF Core context for audit log queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="AuditController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	public AuditController(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>The role of the currently authenticated user.</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>The tenancy ID of the caller, or <c>null</c> for GlobalAdmin.</summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Returns audit log entries, newest first. GlobalAdmin sees all entries;
	/// TenantAdmin sees only entries for their tenancy.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="AuditLogResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller is a TenantUser.
	/// </returns>
	[HttpGet]
	public async Task<IActionResult> List()
	{
		// Reject TenantUser callers up front — they have no audit log access.
		if (CallerRole != "GlobalAdmin" && CallerRole != "TenantAdmin")
		{
			return Forbid();
		}

		var query = _db.AuditLogs.AsQueryable();

		if (CallerRole == "TenantAdmin")
		{
			// Scope the query to the caller's tenancy. GlobalAdmin audit entries
			// have TenancyId = null and are not included in a TenantAdmin's view.
			query = query.Where(a => a.TenancyId == CallerTenancyId);
		}

		// Return entries in reverse-chronological order so the most recent events
		// appear first — the natural order for an audit trail UI.
		var logs = await query
			.OrderByDescending(a => a.Timestamp)
			.Select(a => new AuditLogResponse(a.Id, a.UserId, a.TenancyId, a.Action,
				a.IpAddress, a.SubnetId, a.Timestamp, a.Notes))
			.ToListAsync();

		return Ok(logs);
	}
}
