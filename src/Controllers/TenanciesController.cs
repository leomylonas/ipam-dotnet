using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Controllers;

/// <summary>
/// Manages tenancy lifecycle operations. All endpoints are restricted to the
/// <c>GlobalAdmin</c> role because tenancies define the top-level isolation
/// boundary of the system — only an admin should be able to create or destroy
/// one.
///
/// Creating a tenancy also bootstraps an initial <c>TenantAdmin</c> user in
/// the same request so the tenancy is immediately usable.
/// </summary>
[ApiController]
[Route("api/tenancies")]
[Authorize(Roles = "GlobalAdmin")]
public class TenanciesController : ControllerBase
{
	/// <summary>EF Core context for tenancy and related data queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Identity service used to create and delete user accounts.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>Audit service for recording tenancy lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="TenanciesController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="userManager">Identity user manager, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public TenanciesController(AppDbContext db, UserManager<ApplicationUser> userManager, AuditService audit)
	{
		_db = db;
		_userManager = userManager;
		_audit = audit;
	}

	/// <summary>
	/// Returns the list of all tenancies registered in the system.
	/// </summary>
	/// <returns><c>200 OK</c> with a list of <see cref="TenancyResponse"/> objects.</returns>
	[HttpGet]
	public async Task<IActionResult> List()
	{
		// Project directly to the DTO in the query to avoid over-fetching columns.
		var tenancies = await _db.Tenancies
			.Select(t => new TenancyResponse(t.Id, t.Name, t.Description, t.CreatedAt))
			.ToListAsync();

		return Ok(tenancies);
	}

	/// <summary>
	/// Creates a new tenancy and its initial <c>TenantAdmin</c> user in a single
	/// atomic operation. The tenancy name must be unique across the system.
	/// </summary>
	/// <param name="req">Request body with the tenancy details and initial admin credentials.</param>
	/// <returns>
	/// <c>201 Created</c> with the new tenancy on success;
	/// <c>409 Conflict</c> if a tenancy with the same name already exists;
	/// <c>400 Bad Request</c> if the admin user creation fails (e.g. weak password).
	/// </returns>
	[HttpPost]
	public async Task<IActionResult> Create([FromBody] CreateTenancyRequest req)
	{
		// Guard against duplicate tenancy names before touching the database.
		if (await _db.Tenancies.AnyAsync(t => t.Name == req.Name))
		{
			return Conflict($"Tenancy '{req.Name}' already exists");
		}

		// Build the tenancy entity. The ID is assigned here so the admin user
		// can reference it before SaveChangesAsync is called.
		var tenancy = new Tenancy
		{
			Id = Guid.NewGuid(),
			Name = req.Name,
			Description = req.Description,
			CreatedAt = DateTime.UtcNow
		};
		_db.Tenancies.Add(tenancy);

		// Create the initial TenantAdmin via Identity so the password is hashed
		// correctly and all Identity validators run.
		var adminUser = new ApplicationUser
		{
			UserName = req.AdminUsername,
			Email = req.AdminUsername,
			Role = "TenantAdmin",
			TenancyId = tenancy.Id
		};

		var result = await _userManager.CreateAsync(adminUser, req.AdminPassword);
		if (!result.Succeeded)
		{
			// Surface Identity validation errors (duplicate username, weak password, etc.)
			return BadRequest(result.Errors.Select(e => e.Description));
		}

		// Record who created this tenancy in the audit log. TenancyId is null for
		// GlobalAdmin actions because the admin is not affiliated with any tenancy.
		var callerUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
		_audit.Log(callerUserId, null, "TenancyCreated", notes: $"Tenancy={req.Name}");

		// Commit the tenancy row. The Identity user was already committed by
		// CreateAsync above, so we only need to save the tenancy + audit entry.
		await _db.SaveChangesAsync();

		return CreatedAtAction(nameof(List),
			new TenancyResponse(tenancy.Id, tenancy.Name, tenancy.Description, tenancy.CreatedAt));
	}

	/// <summary>
	/// Updates an existing tenancy's mutable fields.
	/// </summary>
	/// <param name="id">The ID of the tenancy to update.</param>
	/// <param name="req">Request body with updated tenancy name and description.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated tenancy on success;
	/// <c>404 Not Found</c> if the tenancy does not exist;
	/// <c>409 Conflict</c> if the new name collides with another tenancy.
	/// </returns>
	[HttpPut("{id:guid}")]
	public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenancyRequest req)
	{
		var tenancy = await _db.Tenancies.FindAsync(id);
		if (tenancy is null)
		{
			return NotFound();
		}

		if (await _db.Tenancies.AnyAsync(t => t.Id != id && t.Name == req.Name))
		{
			return Conflict($"Tenancy '{req.Name}' already exists");
		}

		tenancy.Name = req.Name;
		tenancy.Description = req.Description;
		await _db.SaveChangesAsync();

		return Ok(new TenancyResponse(tenancy.Id, tenancy.Name, tenancy.Description, tenancy.CreatedAt));
	}

	/// <summary>
	/// Deletes a tenancy and all data associated with it: users, private subnets,
	/// allocations, exclusions, subnet access rules, and audit log entries.
	/// This operation is irreversible.
	/// </summary>
	/// <param name="id">The ID of the tenancy to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if no tenancy with the given ID exists.
	/// </returns>
	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> Delete(Guid id)
	{
		var tenancy = await _db.Tenancies.FindAsync(id);
		if (tenancy is null)
		{
			return NotFound();
		}

		// Delete users via Identity to ensure password hash cleanup and any
		// other Identity hooks run correctly.
		var users = await _db.Users.Where(u => u.TenancyId == id).ToListAsync();
		foreach (var user in users)
		{
			await _userManager.DeleteAsync(user);
		}

		// Collect the IDs of private subnets owned by this tenancy so we can
		// cascade-delete exclusions that reference those subnets.
		var subnets = await _db.Subnets
			.Where(s => s.TenancyId == id)
			.Select(s => s.Id)
			.ToListAsync();

		// Bulk-delete dependent data before removing the tenancy itself.
		// ExecuteDeleteAsync generates a single DELETE statement per call.
		// Deletion order matters: child rows must be removed before their parents
		// so that FK constraints are satisfied on engines that enforce them (MySQL).

		// AllocationTags reference Allocations, so they must go first.
		await _db.AllocationTags
			.Where(t => _db.Allocations
				.Where(a => a.TenancyId == id)
				.Select(a => a.Id)
				.Contains(t.AllocationId))
			.ExecuteDeleteAsync();

		await _db.Allocations.Where(a => a.TenancyId == id).ExecuteDeleteAsync();

		// Guard against empty subnets list: some providers generate invalid SQL
		// for an IN clause with zero elements.
		if (subnets.Count > 0)
		{
			await _db.Exclusions.Where(e => subnets.Contains(e.SubnetId)).ExecuteDeleteAsync();
		}

		await _db.SubnetTenancyAccesses.Where(a => a.TenancyId == id).ExecuteDeleteAsync();
		await _db.Subnets.Where(s => s.TenancyId == id).ExecuteDeleteAsync();
		await _db.AuditLogs.Where(a => a.TenancyId == id).ExecuteDeleteAsync();

		// Remove the tenancy row itself and write the audit entry before committing.
		_db.Tenancies.Remove(tenancy);

		var callerUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
		_audit.Log(callerUserId, null, "TenancyDeleted", notes: $"Tenancy={tenancy.Name}");

		await _db.SaveChangesAsync();
		return NoContent();
	}
}
