using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Provides read-only access to the audit log. The audit log is an append-only
/// record of significant system events written automatically by services during
/// mutations. It cannot be modified via the API.
///
/// Access rules (enforced by <see cref="AuditService.ListAsync"/>):
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Sees all audit entries across all tenancies.</description></item>
///   <item><term>TenantAdmin</term><description>Sees only audit entries for their own tenancy.</description></item>
///   <item><term>TenantUser</term><description>Cannot access the audit log.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController : IpamControllerBase
{
	/// <summary>Audit service that owns the query logic and access rules.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="AuditController"/>.
	/// </summary>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public AuditController(AuditService audit)
	{
		_audit = audit;
	}

	/// <summary>
	/// Returns audit log entries, newest first.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="AuditLogResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller is a TenantUser.
	/// </returns>
	[HttpGet]
	public Task<IActionResult> List() =>
		ExecuteAsync(async () => Ok(await _audit.ListAsync(GetCaller())));
}
