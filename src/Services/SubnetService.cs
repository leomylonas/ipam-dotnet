using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Manages both shared and private subnets, including their creation, update,
/// deletion, and (for shared subnets) tenancy-level access grants. CIDR parsing
/// and overlap detection are delegated to <see cref="SubnetValidationService"/>.
///
/// Shared subnet operations are restricted to GlobalAdmin at the controller level
/// via <c>[Authorize(Roles = "GlobalAdmin")]</c>; private subnet operations enforce
/// the tenancy-scoped rules internally.
///
/// Registered as a scoped service.
/// </summary>
public class SubnetService
{
	/// <summary>EF Core context for subnet and access control queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Service for CIDR parsing, RFC1918 validation, and overlap detection.</summary>
	private readonly SubnetValidationService _validation;

	/// <summary>Audit service for staging subnet lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="SubnetService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="validation">Subnet validation service, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public SubnetService(AppDbContext db, SubnetValidationService validation, AuditService audit)
	{
		_db = db;
		_validation = validation;
		_audit = audit;
	}

	// ── Shared subnets ────────────────────────────────────────────────────────

	/// <summary>
	/// Returns all shared subnets accessible to the caller. GlobalAdmin sees all
	/// shared subnets. Tenant callers see only subnets that are either open to all
	/// (no access restrictions) or explicitly granted to their tenancy.
	/// </summary>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A list of accessible shared <see cref="SubnetResponse"/> objects.</returns>
	public async Task<List<SubnetResponse>> ListSharedAsync(CallerContext caller)
	{
		// Start with all shared subnets.
		var query = _db.Subnets.Where(s => s.Type == SubnetType.Shared);

		if (!caller.IsGlobalAdmin && caller.TenancyId.HasValue)
		{
			var tenancyId = caller.TenancyId.Value;

			// Include only subnets that are:
			//   (a) open to all — no SubnetTenancyAccess rows for this subnet, OR
			//   (b) explicitly granted to the caller's tenancy.
			query = query.Where(s =>
				!_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id) ||
				_db.SubnetTenancyAccesses.Any(a => a.SubnetId == s.Id && a.TenancyId == tenancyId));
		}

		return await query
			.Select(s => new SubnetResponse(s.Id, s.Cidr, s.Name, s.Description,
				s.Type.ToString(), s.TenancyId, s.CreatedAt))
			.ToListAsync();
	}

	/// <summary>
	/// Creates a new shared subnet. The CIDR must be valid and must not overlap
	/// any existing shared subnet. Shared subnets are not required to be RFC1918 —
	/// GlobalAdmin may create subnets in public address space if needed.
	/// </summary>
	/// <param name="req">Request body with CIDR, name, and description.</param>
	/// <param name="callerId">Identity ID of the GlobalAdmin, used for audit.</param>
	/// <returns>The newly created subnet as a <see cref="SubnetResponse"/>.</returns>
	/// <exception cref="ValidationException">Thrown if the CIDR is not valid.</exception>
	/// <exception cref="ConflictException">Thrown if the CIDR overlaps an existing shared subnet.</exception>
	public async Task<SubnetResponse> CreateSharedAsync(CreateSubnetRequest req, string callerId)
	{
		// Validate the CIDR syntax before doing any further checks.
		if (!_validation.TryParseCidr(req.Cidr, out _))
		{
			throw new ValidationException("Invalid CIDR notation");
		}

		// Check that the new CIDR does not overlap any existing shared subnet.
		var overlap = await _validation.CheckOverlapAsync(req.Cidr, SubnetType.Shared, null);
		if (overlap is not null)
		{
			throw new ConflictException(overlap);
		}

		var subnet = new Subnet
		{
			Id = Guid.NewGuid(),
			Cidr = req.Cidr,
			Name = req.Name,
			Description = req.Description,
			Type = SubnetType.Shared,
			TenancyId = null,   // Shared subnets have no owning tenancy.
			CreatedAt = DateTime.UtcNow
		};
		_db.Subnets.Add(subnet);
		_audit.Log(callerId, null, "SubnetCreated", subnetId: subnet.Id, notes: $"Cidr={req.Cidr},Type=Shared");
		await _db.SaveChangesAsync();

		return ToResponse(subnet);
	}

	/// <summary>
	/// Updates the mutable attributes of a shared subnet (name and description).
	/// The CIDR is immutable once a subnet has been created.
	/// </summary>
	/// <param name="id">ID of the shared subnet to update.</param>
	/// <param name="req">New name and description values.</param>
	/// <returns>The updated subnet as a <see cref="SubnetResponse"/>.</returns>
	/// <exception cref="NotFoundException">Thrown if no shared subnet with <paramref name="id"/> exists.</exception>
	public async Task<SubnetResponse> UpdateSharedAsync(Guid id, UpdateSubnetRequest req)
	{
		// Scope the lookup to shared subnets only to prevent cross-type confusion.
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s => s.Id == id && s.Type == SubnetType.Shared)
			?? throw new NotFoundException();

		subnet.Name = req.Name;
		subnet.Description = req.Description;
		await _db.SaveChangesAsync();

		return ToResponse(subnet);
	}

	/// <summary>
	/// Deletes a shared subnet and all associated exclusions, allocations, and
	/// tenancy access grants. This operation is irreversible.
	/// </summary>
	/// <param name="id">ID of the shared subnet to delete.</param>
	/// <param name="callerId">Identity ID of the GlobalAdmin, used for audit.</param>
	/// <exception cref="NotFoundException">Thrown if no shared subnet with <paramref name="id"/> exists.</exception>
	public async Task DeleteSharedAsync(Guid id, string callerId)
	{
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s => s.Id == id && s.Type == SubnetType.Shared)
			?? throw new NotFoundException();

		// Cascade-delete dependent data before removing the subnet row itself.
		// AllocationTags on shared-subnet allocations must also be cleaned up.
		await _db.AllocationTags
			.Where(t => _db.Allocations.Where(a => a.SubnetId == id).Select(a => a.Id).Contains(t.AllocationId))
			.ExecuteDeleteAsync();
		await _db.Allocations.Where(a => a.SubnetId == id).ExecuteDeleteAsync();
		await _db.Exclusions.Where(e => e.SubnetId == id).ExecuteDeleteAsync();
		await _db.SubnetTenancyAccesses.Where(a => a.SubnetId == id).ExecuteDeleteAsync();

		_db.Subnets.Remove(subnet);
		_audit.Log(callerId, null, "SubnetDeleted", subnetId: id, notes: $"Cidr={subnet.Cidr}");
		await _db.SaveChangesAsync();
	}

	/// <summary>
	/// Adds a tenancy-level access restriction to a shared subnet. Once any
	/// restriction exists, only explicitly listed tenancies can allocate from it.
	/// </summary>
	/// <param name="id">ID of the shared subnet to restrict.</param>
	/// <param name="tenancyId">ID of the tenancy to grant access to.</param>
	/// <exception cref="NotFoundException">Thrown if the subnet or tenancy does not exist.</exception>
	/// <exception cref="ConflictException">Thrown if the access grant already exists.</exception>
	public async Task GrantAccessAsync(Guid id, Guid tenancyId)
	{
		// Verify the subnet exists and is shared.
		if (!await _db.Subnets.AnyAsync(s => s.Id == id && s.Type == SubnetType.Shared))
		{
			throw new NotFoundException();
		}

		// Verify the target tenancy exists before adding the grant.
		if (!await _db.Tenancies.AnyAsync(t => t.Id == tenancyId))
		{
			throw new NotFoundException("Tenancy not found");
		}

		// (SubnetId, TenancyId) is the composite PK — a duplicate would throw at DB level;
		// check first to surface a clean 409 instead.
		if (await _db.SubnetTenancyAccesses.AnyAsync(a => a.SubnetId == id && a.TenancyId == tenancyId))
		{
			throw new ConflictException("Access already granted");
		}

		_db.SubnetTenancyAccesses.Add(new SubnetTenancyAccess { SubnetId = id, TenancyId = tenancyId });
		await _db.SaveChangesAsync();
	}

	/// <summary>
	/// Removes a tenancy-level access restriction from a shared subnet. If this
	/// was the last restriction, the subnet becomes open to all tenancies again.
	/// </summary>
	/// <param name="id">ID of the shared subnet.</param>
	/// <param name="tenancyId">ID of the tenancy whose access grant to remove.</param>
	/// <exception cref="NotFoundException">Thrown if the access grant does not exist.</exception>
	public async Task RevokeAccessAsync(Guid id, Guid tenancyId)
	{
		var access = await _db.SubnetTenancyAccesses
			.FirstOrDefaultAsync(a => a.SubnetId == id && a.TenancyId == tenancyId)
			?? throw new NotFoundException();

		_db.SubnetTenancyAccesses.Remove(access);
		await _db.SaveChangesAsync();
	}

	// ── Private subnets ───────────────────────────────────────────────────────

	/// <summary>
	/// Returns the private subnets belonging to the specified tenancy. The caller
	/// must be GlobalAdmin or the TenantAdmin of that tenancy.
	/// </summary>
	/// <param name="tenancyId">ID of the tenancy whose subnets to list.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A list of <see cref="SubnetResponse"/> objects for the tenancy's private subnets.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this tenancy.</exception>
	/// <exception cref="NotFoundException">Thrown if the tenancy does not exist.</exception>
	public async Task<List<SubnetResponse>> ListPrivateAsync(Guid tenancyId, CallerContext caller)
	{
		// Enforce tenancy-level access before returning any data.
		if (!CanAccessTenancy(tenancyId, caller))
		{
			throw new ForbiddenException();
		}

		// Return 404 if the tenancy itself does not exist rather than an empty 200,
		// so callers can distinguish between "no subnets" and "tenancy not found".
		if (!await _db.Tenancies.AnyAsync(t => t.Id == tenancyId))
		{
			throw new NotFoundException();
		}

		return await _db.Subnets
			.Where(s => s.TenancyId == tenancyId && s.Type == SubnetType.Private)
			.Select(s => new SubnetResponse(s.Id, s.Cidr, s.Name, s.Description,
				s.Type.ToString(), s.TenancyId, s.CreatedAt))
			.ToListAsync();
	}

	/// <summary>
	/// Creates a new private subnet within the specified tenancy. The CIDR must be
	/// valid, fall within RFC1918 address space, and not overlap any existing private
	/// subnet in the same tenancy.
	/// </summary>
	/// <param name="tenancyId">ID of the tenancy that will own the subnet.</param>
	/// <param name="req">Request body with CIDR, name, and description.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The newly created subnet as a <see cref="SubnetResponse"/>.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this tenancy.</exception>
	/// <exception cref="NotFoundException">Thrown if the tenancy does not exist.</exception>
	/// <exception cref="ValidationException">Thrown if the CIDR is invalid or not RFC1918.</exception>
	/// <exception cref="ConflictException">Thrown if the CIDR overlaps an existing private subnet in this tenancy.</exception>
	public async Task<SubnetResponse> CreatePrivateAsync(Guid tenancyId, CreateSubnetRequest req, CallerContext caller)
	{
		if (!CanAccessTenancy(tenancyId, caller))
		{
			throw new ForbiddenException();
		}

		if (!await _db.Tenancies.AnyAsync(t => t.Id == tenancyId))
		{
			throw new NotFoundException();
		}

		// Parse the CIDR first — if invalid, report it before performing further checks.
		if (!_validation.TryParseCidr(req.Cidr, out var parsedNetwork) || parsedNetwork is null)
		{
			throw new ValidationException("Invalid CIDR notation");
		}

		// Private subnets must reside within one of the RFC1918 ranges.
		if (!_validation.IsRfc1918(parsedNetwork.Value))
		{
			throw new ValidationException("Private subnets must be within RFC1918 ranges");
		}

		// Reject CIDRs that overlap existing private subnets in the same tenancy.
		var overlap = await _validation.CheckOverlapAsync(req.Cidr, SubnetType.Private, tenancyId);
		if (overlap is not null)
		{
			throw new ConflictException(overlap);
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
		_audit.Log(caller.UserId, tenancyId, "SubnetCreated", subnetId: subnet.Id, notes: $"Cidr={req.Cidr},Type=Private");
		await _db.SaveChangesAsync();

		return ToResponse(subnet);
	}

	/// <summary>
	/// Updates the mutable attributes of a private subnet (name and description).
	/// The CIDR is immutable. The subnet must belong to the specified tenancy.
	/// </summary>
	/// <param name="tenancyId">ID of the owning tenancy (used as a scope guard).</param>
	/// <param name="subnetId">ID of the subnet to update.</param>
	/// <param name="req">New name and description values.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The updated subnet as a <see cref="SubnetResponse"/>.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot manage this tenancy.</exception>
	/// <exception cref="NotFoundException">Thrown if the subnet does not exist in this tenancy.</exception>
	public async Task<SubnetResponse> UpdatePrivateAsync(Guid tenancyId, Guid subnetId, UpdateSubnetRequest req, CallerContext caller)
	{
		if (!CanAccessTenancy(tenancyId, caller))
		{
			throw new ForbiddenException();
		}

		var subnet = await _db.Subnets.FirstOrDefaultAsync(s =>
			s.Id == subnetId && s.TenancyId == tenancyId && s.Type == SubnetType.Private)
			?? throw new NotFoundException();

		subnet.Name = req.Name;
		subnet.Description = req.Description;
		await _db.SaveChangesAsync();

		return ToResponse(subnet);
	}

	/// <summary>
	/// Deletes a private subnet and all associated exclusions and allocations.
	/// The subnet must belong to the specified tenancy.
	/// </summary>
	/// <param name="tenancyId">ID of the owning tenancy (used as a scope guard).</param>
	/// <param name="subnetId">ID of the subnet to delete.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this tenancy.</exception>
	/// <exception cref="NotFoundException">Thrown if the subnet does not exist in this tenancy.</exception>
	public async Task DeletePrivateAsync(Guid tenancyId, Guid subnetId, CallerContext caller)
	{
		if (!CanAccessTenancy(tenancyId, caller))
		{
			throw new ForbiddenException();
		}

		// Scope the lookup to the specified tenancy and type to prevent cross-tenancy deletions.
		var subnet = await _db.Subnets.FirstOrDefaultAsync(s =>
			s.Id == subnetId && s.TenancyId == tenancyId && s.Type == SubnetType.Private)
			?? throw new NotFoundException();

		// Cascade-delete dependent data before removing the subnet itself.
		await _db.AllocationTags
			.Where(t => _db.Allocations.Where(a => a.SubnetId == subnetId).Select(a => a.Id).Contains(t.AllocationId))
			.ExecuteDeleteAsync();
		await _db.Allocations.Where(a => a.SubnetId == subnetId).ExecuteDeleteAsync();
		await _db.Exclusions.Where(e => e.SubnetId == subnetId).ExecuteDeleteAsync();

		_db.Subnets.Remove(subnet);
		_audit.Log(caller.UserId, tenancyId, "SubnetDeleted", subnetId: subnetId, notes: $"Cidr={subnet.Cidr}");
		await _db.SaveChangesAsync();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns <c>true</c> when the caller is authorised to access the specified tenancy.
	/// GlobalAdmin can access any tenancy; TenantAdmin can only access their own.
	/// TenantUser can never access tenancy-level operations.
	/// </summary>
	/// <param name="tenancyId">The tenancy being accessed.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns><c>true</c> if access is permitted; <c>false</c> otherwise.</returns>
	private static bool CanAccessTenancy(Guid tenancyId, CallerContext caller) =>
		caller.IsGlobalAdmin || (caller.IsTenantAdmin && caller.TenancyId == tenancyId);

	/// <summary>
	/// Projects a <see cref="Subnet"/> entity to the standard <see cref="SubnetResponse"/> DTO.
	/// Centralised here so the projection logic is not duplicated across methods.
	/// </summary>
	/// <param name="subnet">The entity to project.</param>
	/// <returns>The corresponding <see cref="SubnetResponse"/>.</returns>
	private static SubnetResponse ToResponse(Subnet subnet) =>
		new(subnet.Id, subnet.Cidr, subnet.Name, subnet.Description,
			subnet.Type.ToString(), subnet.TenancyId, subnet.CreatedAt);
}
