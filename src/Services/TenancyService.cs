using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Manages the full lifecycle of tenancies: listing, creation (including the
/// mandatory initial TenantAdmin), name/description updates, and cascading
/// deletion of all associated data.
///
/// All public methods assume the caller has already been authorised at the HTTP
/// layer (via <c>[Authorize(Roles = "GlobalAdmin")]</c>) — they do not repeat
/// the GlobalAdmin role check internally.
///
/// Registered as a scoped service so it shares the EF context and audit service
/// with other services in the same request.
/// </summary>
public class TenancyService
{
	/// <summary>EF Core context used to query and persist tenancy data.</summary>
	private readonly AppDbContext _db;

	/// <summary>Identity service used to create and delete TenantAdmin users atomically with tenancies.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>Audit service for staging tenancy lifecycle events alongside the save.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="TenancyService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="userManager">Identity user manager, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public TenancyService(AppDbContext db, UserManager<ApplicationUser> userManager, AuditService audit)
	{
		_db = db;
		_userManager = userManager;
		_audit = audit;
	}

	/// <summary>
	/// Returns all tenancies in the system, projected to response DTOs.
	/// </summary>
	/// <returns>A list of all <see cref="TenancyResponse"/> objects, unordered.</returns>
	public async Task<List<TenancyResponse>> ListAsync()
	{
		// Project directly in the query to avoid loading entity properties not needed by the caller.
		return await _db.Tenancies
			.Select(t => new TenancyResponse(t.Id, t.Name, t.Description, t.CreatedAt))
			.ToListAsync();
	}

	/// <summary>
	/// Creates a new tenancy and its initial TenantAdmin user in a single logical operation.
	/// The tenancy is persisted via EF and the admin user via Identity; both succeed
	/// or the caller receives an error before anything is committed.
	/// </summary>
	/// <param name="req">Request data containing the tenancy name and admin credentials.</param>
	/// <param name="callerId">Identity ID of the GlobalAdmin performing the creation, used for audit.</param>
	/// <returns>The newly created tenancy as a <see cref="TenancyResponse"/>.</returns>
	/// <exception cref="ConflictException">Thrown if a tenancy with <paramref name="req.Name"/> already exists.</exception>
	/// <exception cref="IdentityOperationException">Thrown if Identity rejects the admin credentials (e.g. weak password).</exception>
	public async Task<TenancyResponse> CreateAsync(CreateTenancyRequest req, string callerId)
	{
		// Guard against duplicate tenancy names before touching the database further.
		if (await _db.Tenancies.AnyAsync(t => t.Name == req.Name))
		{
			throw new ConflictException($"Tenancy '{req.Name}' already exists");
		}

		// Assign the ID here so the admin user can reference the tenancy before it is saved.
		var tenancy = new Tenancy
		{
			Id = Guid.NewGuid(),
			Name = req.Name,
			Description = req.Description,
			CreatedAt = DateTime.UtcNow
		};
		_db.Tenancies.Add(tenancy);

		// Create the initial TenantAdmin via Identity so passwords are hashed correctly
		// and all Identity validators (duplicate username, password policy) run.
		var adminUser = new ApplicationUser
		{
			UserName = req.AdminUsername,
			Email = req.AdminUsername,
			Role = Roles.TenantAdmin,
			TenancyId = tenancy.Id
		};

		var result = await _userManager.CreateAsync(adminUser, req.AdminPassword);
		if (!result.Succeeded)
		{
			// Surface Identity validation errors so the caller knows what to fix.
			throw new IdentityOperationException(result.Errors.Select(e => e.Description));
		}

		// GlobalAdmin actions have no tenancy affiliation — TenancyId is null in the audit entry.
		_audit.Log(callerId, null, "TenancyCreated", notes: $"Tenancy={req.Name}");

		// Commit the tenancy row and audit entry. Identity's CreateAsync already committed
		// the admin user, so SaveChangesAsync here handles the tenancy and audit log rows.
		await _db.SaveChangesAsync();

		return new TenancyResponse(tenancy.Id, tenancy.Name, tenancy.Description, tenancy.CreatedAt);
	}

	/// <summary>
	/// Updates the mutable fields of an existing tenancy: name and description.
	/// The tenancy ID and creation timestamp are immutable.
	/// </summary>
	/// <param name="id">ID of the tenancy to update.</param>
	/// <param name="req">The new values for name and description.</param>
	/// <returns>The updated tenancy as a <see cref="TenancyResponse"/>.</returns>
	/// <exception cref="NotFoundException">Thrown if no tenancy with <paramref name="id"/> exists.</exception>
	/// <exception cref="ConflictException">Thrown if <paramref name="req.Name"/> is already taken by another tenancy.</exception>
	public async Task<TenancyResponse> UpdateAsync(Guid id, UpdateTenancyRequest req)
	{
		// Null-coalescing throw: pattern avoids a separate null check and keeps the intent clear.
		var tenancy = await _db.Tenancies.FindAsync(id)
			?? throw new NotFoundException();

		// The new name must not collide with any tenancy other than this one.
		if (await _db.Tenancies.AnyAsync(t => t.Id != id && t.Name == req.Name))
		{
			throw new ConflictException($"Tenancy '{req.Name}' already exists");
		}

		tenancy.Name = req.Name;
		tenancy.Description = req.Description;
		await _db.SaveChangesAsync();

		return new TenancyResponse(tenancy.Id, tenancy.Name, tenancy.Description, tenancy.CreatedAt);
	}

	/// <summary>
	/// Deletes a tenancy and all data associated with it: users, private subnets,
	/// exclusions, allocations, allocation tags, subnet access grants, and audit
	/// log entries. This operation is irreversible.
	/// </summary>
	/// <param name="id">ID of the tenancy to delete.</param>
	/// <param name="callerId">Identity ID of the GlobalAdmin performing the deletion, used for audit.</param>
	/// <exception cref="NotFoundException">Thrown if no tenancy with <paramref name="id"/> exists.</exception>
	public async Task DeleteAsync(Guid id, string callerId)
	{
		var tenancy = await _db.Tenancies.FindAsync(id)
			?? throw new NotFoundException();

		// Delete users via Identity so password hash cleanup and any registered hooks run.
		var users = await _db.Users.Where(u => u.TenancyId == id).ToListAsync();
		foreach (var user in users)
		{
			await _userManager.DeleteAsync(user);
		}

		// Collect private subnet IDs upfront so we can cascade-delete their exclusions.
		var subnets = await _db.Subnets
			.Where(s => s.TenancyId == id)
			.Select(s => s.Id)
			.ToListAsync();

		// Deletion order matters: child rows must be removed before their parents
		// to satisfy FK constraints on databases that enforce them (MySQL, PostgreSQL).

		// AllocationTags reference Allocations — must be deleted first.
		await _db.AllocationTags
			.Where(t => _db.Allocations
				.Where(a => a.TenancyId == id)
				.Select(a => a.Id)
				.Contains(t.AllocationId))
			.ExecuteDeleteAsync();

		await _db.Allocations.Where(a => a.TenancyId == id).ExecuteDeleteAsync();

		// Guard against an empty subnets list: some providers generate invalid SQL
		// for an IN clause with zero elements.
		if (subnets.Count > 0)
		{
			await _db.Exclusions.Where(e => subnets.Contains(e.SubnetId)).ExecuteDeleteAsync();
		}

		await _db.SubnetTenancyAccesses.Where(a => a.TenancyId == id).ExecuteDeleteAsync();
		await _db.Subnets.Where(s => s.TenancyId == id).ExecuteDeleteAsync();
		await _db.AuditLogs.Where(a => a.TenancyId == id).ExecuteDeleteAsync();

		// Stage the tenancy removal and the audit entry; commit both together.
		_db.Tenancies.Remove(tenancy);
		_audit.Log(callerId, null, "TenancyDeleted", notes: $"Tenancy={tenancy.Name}");
		await _db.SaveChangesAsync();
	}
}
