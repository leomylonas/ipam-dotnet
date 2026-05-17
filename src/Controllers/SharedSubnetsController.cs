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
/// Manages shared subnets — subnets that are created and owned by GlobalAdmin
/// and may be made accessible to one or more tenancies, or to all tenancies
/// when no explicit access restriction exists.
///
/// Access control summary:
/// <list type="bullet">
///   <item><term>List</term><description>All authenticated users; filtered by tenancy access for non-admins.</description></item>
///   <item><term>Create / Delete / GrantAccess / RevokeAccess</term><description>GlobalAdmin only.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/subnets/shared")]
[Authorize]
public class SharedSubnetsController : ControllerBase
{
	/// <summary>EF Core context for subnet and access control queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Service for CIDR parsing and overlap validation.</summary>
	private readonly SubnetValidationService _validation;

	/// <summary>Audit service for recording subnet lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="SharedSubnetsController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="validation">Subnet validation service, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public SharedSubnetsController(AppDbContext db, SubnetValidationService validation, AuditService audit)
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
	/// Returns all shared subnets accessible to the caller. GlobalAdmin sees
	/// everything. Tenant users see subnets that either have no access restrictions
	/// at all (open to all) or have an explicit grant for their tenancy.
	/// </summary>
	/// <returns><c>200 OK</c> with a list of <see cref="SubnetResponse"/> objects.</returns>
	[HttpGet]
	public async Task<IActionResult> List()
	{
		// Start with all shared subnets.
		var query = _db.Subnets.Where(s => s.Type == SubnetType.Shared);

		if (CallerRole != "GlobalAdmin" && CallerTenancyId.HasValue)
		{
			var tenancyId = CallerTenancyId.Value;

			// Include only subnets that are:
			//   (a) open to all (no SubnetTenancyAccess rows for this subnet), OR
			//   (b) explicitly granted to the caller's tenancy.
			query = query.Where(s =>
				!_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id) ||
				_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id && a.TenancyId == tenancyId));
		}

		var subnets = await query
			.Select(s => new SubnetResponse(s.Id, s.Cidr, s.Name, s.Description,
				s.Type.ToString(), s.TenancyId, s.CreatedAt))
			.ToListAsync();

		return Ok(subnets);
	}

	/// <summary>
	/// Creates a new shared subnet. The CIDR must be valid and must not overlap
	/// with any existing shared subnet. Shared subnets are not required to be
	/// RFC1918 — GlobalAdmin can create public-range subnets if needed.
	/// </summary>
	/// <param name="req">Request body with the CIDR, name, and description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new subnet on success;
	/// <c>400 Bad Request</c> if the CIDR is invalid;
	/// <c>409 Conflict</c> if the CIDR overlaps an existing shared subnet.
	/// </returns>
	[HttpPost]
	[Authorize(Roles = "GlobalAdmin")]
	public async Task<IActionResult> Create([FromBody] CreateSubnetRequest req)
	{
		// Validate the CIDR syntax before doing any overlap checking.
		if (!_validation.TryParseCidr(req.Cidr, out _))
		{
			return BadRequest("Invalid CIDR notation");
		}

		// Check that this CIDR does not overlap any existing shared subnet.
		var overlap = await _validation.CheckOverlapAsync(req.Cidr, SubnetType.Shared, null);
		if (overlap is not null)
		{
			return Conflict(overlap);
		}

		// Build and persist the subnet entity.
		var subnet = new Subnet
		{
			Id = Guid.NewGuid(),
			Cidr = req.Cidr,
			Name = req.Name,
			Description = req.Description,
			Type = SubnetType.Shared,
			TenancyId = null,       // Shared subnets have no owning tenancy.
			CreatedAt = DateTime.UtcNow
		};
		_db.Subnets.Add(subnet);
		_audit.Log(CallerId, null, "SubnetCreated", subnetId: subnet.Id, notes: $"Cidr={req.Cidr},Type=Shared");
		await _db.SaveChangesAsync();

		return CreatedAtAction(nameof(List),
			new SubnetResponse(subnet.Id, subnet.Cidr, subnet.Name, subnet.Description,
				subnet.Type.ToString(), subnet.TenancyId, subnet.CreatedAt));
	}

	/// <summary>
	/// Updates mutable attributes of a shared subnet. CIDR is immutable.
	/// </summary>
	/// <param name="id">The ID of the shared subnet to update.</param>
	/// <param name="req">Request body with updated name and description.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated subnet on success;
	/// <c>404 Not Found</c> if the subnet does not exist or is not shared.
	/// </returns>
	[HttpPut("{id:guid}")]
	[Authorize(Roles = "GlobalAdmin")]
	public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSubnetRequest req)
	{
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s => s.Id == id && s.Type == SubnetType.Shared);
		if (subnet is null)
		{
			return NotFound();
		}

		subnet.Name = req.Name;
		subnet.Description = req.Description;
		await _db.SaveChangesAsync();

		return Ok(new SubnetResponse(subnet.Id, subnet.Cidr, subnet.Name, subnet.Description,
			subnet.Type.ToString(), subnet.TenancyId, subnet.CreatedAt));
	}

	/// <summary>
	/// Deletes a shared subnet and all its associated exclusions, allocations,
	/// and tenancy access rules. This operation is irreversible.
	/// </summary>
	/// <param name="id">The ID of the shared subnet to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the subnet does not exist or is not a shared subnet.
	/// </returns>
	[HttpDelete("{id:guid}")]
	[Authorize(Roles = "GlobalAdmin")]
	public async Task<IActionResult> Delete(Guid id)
	{
		// Scope the lookup to shared subnets only so a private subnet ID cannot
		// be passed to accidentally delete something outside admin scope.
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s => s.Id == id && s.Type == SubnetType.Shared);
		if (subnet is null)
		{
			return NotFound();
		}

		// Cascade-delete dependent data before removing the subnet row itself.
		await _db.Exclusions.Where(e => e.SubnetId == id).ExecuteDeleteAsync();
		await _db.Allocations.Where(a => a.SubnetId == id).ExecuteDeleteAsync();
		await _db.SubnetTenancyAccesses.Where(a => a.SubnetId == id).ExecuteDeleteAsync();
		_db.Subnets.Remove(subnet);
		_audit.Log(CallerId, null, "SubnetDeleted", subnetId: id, notes: $"Cidr={subnet.Cidr}");
		await _db.SaveChangesAsync();

		return NoContent();
	}

	/// <summary>
	/// Adds an access restriction that limits a shared subnet to a specific tenancy.
	/// Once any restriction exists for a subnet, only explicitly granted tenancies
	/// can access it.
	/// </summary>
	/// <param name="id">The ID of the shared subnet to restrict.</param>
	/// <param name="req">Request body identifying the tenancy to grant access to.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the subnet or tenancy does not exist;
	/// <c>409 Conflict</c> if access for this tenancy is already granted.
	/// </returns>
	[HttpPost("{id:guid}/access")]
	[Authorize(Roles = "GlobalAdmin")]
	public async Task<IActionResult> GrantAccess(Guid id, [FromBody] GrantSubnetAccessRequest req)
	{
		// Verify the subnet exists and is a shared subnet.
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s => s.Id == id && s.Type == SubnetType.Shared);
		if (subnet is null)
		{
			return NotFound();
		}

		// Verify the target tenancy exists.
		if (!await _db.Tenancies.AnyAsync(t => t.Id == req.TenancyId))
		{
			return NotFound("Tenancy not found");
		}

		// Prevent duplicate access grants — the combination (SubnetId, TenancyId) is
		// the primary key of SubnetTenancyAccess so inserting a duplicate would throw.
		if (await _db.SubnetTenancyAccesses.AnyAsync(a => a.SubnetId == id && a.TenancyId == req.TenancyId))
		{
			return Conflict("Access already granted");
		}

		_db.SubnetTenancyAccesses.Add(new SubnetTenancyAccess { SubnetId = id, TenancyId = req.TenancyId });
		await _db.SaveChangesAsync();

		return NoContent();
	}

	/// <summary>
	/// Removes an existing tenancy-level access restriction from a shared subnet.
	/// If this was the last restriction, the subnet becomes open to all tenancies.
	/// </summary>
	/// <param name="id">The ID of the shared subnet.</param>
	/// <param name="tenancyId">The ID of the tenancy whose access should be revoked.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the access grant does not exist.
	/// </returns>
	[HttpDelete("{id:guid}/access/{tenancyId:guid}")]
	[Authorize(Roles = "GlobalAdmin")]
	public async Task<IActionResult> RevokeAccess(Guid id, Guid tenancyId)
	{
		// Look up the specific grant row to confirm it exists before removing it.
		var access = await _db.SubnetTenancyAccesses
			.FirstOrDefaultAsync(a => a.SubnetId == id && a.TenancyId == tenancyId);

		if (access is null)
		{
			return NotFound();
		}

		_db.SubnetTenancyAccesses.Remove(access);
		await _db.SaveChangesAsync();

		return NoContent();
	}
}
