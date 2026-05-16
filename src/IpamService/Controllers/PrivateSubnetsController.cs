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
/// Manages private subnets within a specific tenancy. Private subnets are
/// exclusively owned by one tenancy and must fall within RFC1918 address space.
/// They are not visible or accessible to other tenancies.
///
/// Access rules:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Can manage private subnets in any tenancy.</description></item>
///   <item><term>TenantAdmin</term><description>Can manage private subnets only within their own tenancy.</description></item>
///   <item><term>TenantUser</term><description>Cannot manage private subnets.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/tenancies/{tenancyId:guid}/subnets")]
[Authorize]
public class PrivateSubnetsController : ControllerBase
{
	/// <summary>EF Core context for subnet queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Service for CIDR parsing, RFC1918 validation, and overlap checking.</summary>
	private readonly SubnetValidationService _validation;

	/// <summary>Audit service for recording subnet lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="PrivateSubnetsController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="validation">Subnet validation service, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public PrivateSubnetsController(AppDbContext db, SubnetValidationService validation, AuditService audit)
	{
		_db = db;
		_validation = validation;
		_audit = audit;
	}

	/// <summary>The identity ID of the currently authenticated user.</summary>
	private string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

	/// <summary>The role of the currently authenticated user.</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>The tenancy ID of the caller, or <c>null</c> for GlobalAdmin.</summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Returns <c>true</c> if the caller is authorised to access the given tenancy.
	/// GlobalAdmin can access any tenancy; TenantAdmin can only access their own.
	/// </summary>
	/// <param name="tenancyId">The tenancy whose resources are being accessed.</param>
	/// <returns><c>true</c> if access is permitted; <c>false</c> otherwise.</returns>
	private bool CanAccessTenancy(Guid tenancyId) =>
		CallerRole == "GlobalAdmin" || (CallerRole == "TenantAdmin" && CallerTenancyId == tenancyId);

	/// <summary>
	/// Returns the list of private subnets belonging to the specified tenancy.
	/// </summary>
	/// <param name="tenancyId">The ID of the tenancy whose subnets to list.</param>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="SubnetResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller cannot access this tenancy;
	/// <c>404 Not Found</c> if the tenancy does not exist.
	/// </returns>
	[HttpGet]
	public async Task<IActionResult> List(Guid tenancyId)
	{
		if (!CanAccessTenancy(tenancyId))
		{
			return Forbid();
		}

		// Verify the tenancy itself exists before returning an empty list,
		// so the caller gets a 404 rather than a misleading empty 200.
		if (!await _db.Tenancies.AnyAsync(t => t.Id == tenancyId))
		{
			return NotFound();
		}

		var subnets = await _db.Subnets
			.Where(s => s.TenancyId == tenancyId && s.Type == SubnetType.Private)
			.Select(s => new SubnetResponse(s.Id, s.Cidr, s.Name, s.Description,
				s.Type.ToString(), s.TenancyId, s.CreatedAt))
			.ToListAsync();

		return Ok(subnets);
	}

	/// <summary>
	/// Creates a new private subnet within the specified tenancy. The CIDR must
	/// be valid, within RFC1918 address space, and must not overlap any existing
	/// private subnet in the same tenancy.
	/// </summary>
	/// <param name="tenancyId">The ID of the tenancy that will own the subnet.</param>
	/// <param name="req">Request body with the CIDR, name, and description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new subnet on success;
	/// <c>400 Bad Request</c> if the CIDR is invalid or not RFC1918;
	/// <c>403 Forbidden</c> if the caller cannot manage this tenancy;
	/// <c>404 Not Found</c> if the tenancy does not exist;
	/// <c>409 Conflict</c> if the CIDR overlaps an existing subnet in this tenancy.
	/// </returns>
	[HttpPost]
	public async Task<IActionResult> Create(Guid tenancyId, [FromBody] CreateSubnetRequest req)
	{
		if (!CanAccessTenancy(tenancyId))
		{
			return Forbid();
		}

		if (!await _db.Tenancies.AnyAsync(t => t.Id == tenancyId))
		{
			return NotFound();
		}

		// Parse the CIDR first; if it is invalid there is no point doing further checks.
		if (!_validation.TryParseCidr(req.Cidr, out var parsedNetwork) || parsedNetwork is null)
		{
			return BadRequest("Invalid CIDR notation");
		}

		// Private subnets must reside within one of the RFC1918 ranges.
		// .Value is required because TryParseCidr returns IPNetwork? (nullable struct).
		if (!_validation.IsRfc1918(parsedNetwork!.Value))
		{
			return BadRequest("Private subnets must be within RFC1918 ranges");
		}

		// Check for CIDR overlap within this tenancy's private subnets.
		var overlap = await _validation.CheckOverlapAsync(req.Cidr, SubnetType.Private, tenancyId);
		if (overlap is not null)
		{
			return Conflict(overlap);
		}

		var subnet = new Subnet
		{
			Id = Guid.NewGuid(),
			Cidr = req.Cidr,
			Name = req.Name,
			Description = req.Description,
			Type = SubnetType.Private,
			TenancyId = tenancyId,
			CreatedAt = DateTime.UtcNow
		};
		_db.Subnets.Add(subnet);
		_audit.Log(CallerId, tenancyId, "SubnetCreated", subnetId: subnet.Id, notes: $"Cidr={req.Cidr},Type=Private");
		await _db.SaveChangesAsync();

		return CreatedAtAction(nameof(List), new { tenancyId },
			new SubnetResponse(subnet.Id, subnet.Cidr, subnet.Name, subnet.Description,
				subnet.Type.ToString(), subnet.TenancyId, subnet.CreatedAt));
	}

	/// <summary>
	/// Deletes a private subnet and all associated exclusions and allocations.
	/// The subnet must belong to the specified tenancy.
	/// </summary>
	/// <param name="tenancyId">The ID of the owning tenancy (used as a scope guard).</param>
	/// <param name="subnetId">The ID of the subnet to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot access this tenancy;
	/// <c>404 Not Found</c> if the subnet does not exist in this tenancy.
	/// </returns>
	[HttpDelete("{subnetId:guid}")]
	public async Task<IActionResult> Delete(Guid tenancyId, Guid subnetId)
	{
		if (!CanAccessTenancy(tenancyId))
		{
			return Forbid();
		}

		// Scope the lookup to the specified tenancy and type to prevent cross-tenancy
		// deletions via a guessed subnet ID.
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s =>
			s.Id == subnetId && s.TenancyId == tenancyId && s.Type == SubnetType.Private);

		if (subnet is null)
		{
			return NotFound();
		}

		// Cascade-delete dependent data before removing the subnet itself.
		await _db.Exclusions.Where(e => e.SubnetId == subnetId).ExecuteDeleteAsync();
		await _db.Allocations.Where(a => a.SubnetId == subnetId).ExecuteDeleteAsync();
		_db.Subnets.Remove(subnet);
		_audit.Log(CallerId, tenancyId, "SubnetDeleted", subnetId: subnetId, notes: $"Cidr={subnet.Cidr}");
		await _db.SaveChangesAsync();

		return NoContent();
	}
}
