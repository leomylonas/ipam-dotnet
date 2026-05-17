using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Manages user account operations: listing, creation, update, deletion, and
/// password management. Role-based authorization rules are enforced here so
/// that permission logic is co-located with the data access it governs rather
/// than scattered across controller action methods.
///
/// Authorization summary:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Unrestricted access to all user operations across all tenancies.</description></item>
///   <item><term>TenantAdmin</term><description>Can only list and manage <c>TenantUser</c> accounts within their own tenancy.</description></item>
///   <item><term>TenantUser</term><description>No user management rights; can only change their own password via <see cref="ChangeOwnPasswordAsync"/>.</description></item>
/// </list>
///
/// Registered as a scoped service.
/// </summary>
public class UserService
{
	/// <summary>EF Core context used to query user data.</summary>
	private readonly AppDbContext _db;

	/// <summary>Identity service used to create, update, delete, and change passwords.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>Audit service for staging user lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="UserService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="userManager">Identity user manager, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public UserService(AppDbContext db, UserManager<ApplicationUser> userManager, AuditService audit)
	{
		_db = db;
		_userManager = userManager;
		_audit = audit;
	}

	/// <summary>
	/// Returns the list of users visible to the caller. GlobalAdmin sees all users;
	/// TenantAdmin sees only users within their own tenancy; TenantUser is forbidden.
	/// </summary>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A list of <see cref="UserResponse"/> objects.</returns>
	/// <exception cref="ForbiddenException">Thrown when the caller is a TenantUser.</exception>
	public async Task<List<UserResponse>> ListAsync(CallerContext caller)
	{
		// TenantUser has no visibility into user listings — reject early.
		if (caller.IsTenantUser)
		{
			throw new ForbiddenException();
		}

		// Start with all application users (excluding IdentityUser sub-types).
		var query = _db.Users.OfType<ApplicationUser>();

		if (caller.IsTenantAdmin)
		{
			// TenantAdmin can only see users that belong to their own tenancy.
			query = query.Where(u => u.TenancyId == caller.TenancyId);
		}

		// GlobalAdmin falls through to the unfiltered query and sees everyone.
		return await query
			.Select(u => new UserResponse(u.Id, u.UserName!, u.Role, u.TenancyId))
			.ToListAsync();
	}

	/// <summary>
	/// Creates a new user account. GlobalAdmin can create any role in any tenancy.
	/// TenantAdmin can only create <c>TenantUser</c> accounts within their own tenancy.
	/// </summary>
	/// <param name="req">Request body containing credentials, role, and tenancy affiliation.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The newly created user as a <see cref="UserResponse"/>.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller lacks permission for the requested role or tenancy.</exception>
	/// <exception cref="IdentityOperationException">Thrown if Identity rejects the password or username.</exception>
	public async Task<UserResponse> CreateAsync(CreateUserRequest req, CallerContext caller)
	{
		if (caller.IsTenantAdmin)
		{
			// TenantAdmin may only create TenantUser accounts — role escalation is not permitted.
			if (req.Role != Roles.TenantUser)
			{
				throw new ForbiddenException();
			}

			// TenantAdmin cannot create users in other tenancies.
			if (req.TenancyId != caller.TenancyId)
			{
				throw new ForbiddenException();
			}
		}
		else if (caller.IsTenantUser)
		{
			// TenantUser cannot create accounts at all.
			throw new ForbiddenException();
		}

		// GlobalAdmin: no restrictions, fall through.

		var user = new ApplicationUser
		{
			UserName = req.Username,
			Email = req.Username,
			Role = req.Role,
			TenancyId = req.TenancyId
		};

		var result = await _userManager.CreateAsync(user, req.Password);
		if (!result.Succeeded)
		{
			throw new IdentityOperationException(result.Errors.Select(e => e.Description));
		}

		_audit.Log(caller.UserId, caller.TenancyId, "UserCreated",
			notes: $"Username={req.Username},Role={req.Role}");
		await _db.SaveChangesAsync();

		return new UserResponse(user.Id, user.UserName!, user.Role, user.TenancyId);
	}

	/// <summary>
	/// Updates mutable profile fields for an existing user. Password changes are
	/// handled by dedicated endpoints and are not processed here.
	/// </summary>
	/// <param name="id">Identity ID of the user to update.</param>
	/// <param name="req">Request body containing the new username, role, and tenancy.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The updated user as a <see cref="UserResponse"/>.</returns>
	/// <exception cref="NotFoundException">Thrown if the user does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller lacks permission for the requested change.</exception>
	/// <exception cref="IdentityOperationException">Thrown if Identity rejects the username update.</exception>
	public async Task<UserResponse> UpdateAsync(string id, UpdateUserRequest req, CallerContext caller)
	{
		var user = await _userManager.FindByIdAsync(id)
			?? throw new NotFoundException();

		if (caller.IsTenantAdmin)
		{
			// TenantAdmin can only update users in their own tenancy to TenantUser role.
			// Moving a user to another tenancy or escalating their role is not permitted.
			if (user.TenancyId != caller.TenancyId
				|| req.TenancyId != caller.TenancyId
				|| req.Role != Roles.TenantUser)
			{
				throw new ForbiddenException();
			}
		}
		else if (caller.IsTenantUser)
		{
			// TenantUser cannot update any account.
			throw new ForbiddenException();
		}

		// GlobalAdmin: no restrictions.

		user.UserName = req.Username;
		user.Email = req.Username;
		user.Role = req.Role;
		user.TenancyId = req.TenancyId;

		var result = await _userManager.UpdateAsync(user);
		if (!result.Succeeded)
		{
			throw new IdentityOperationException(result.Errors.Select(e => e.Description));
		}

		return new UserResponse(user.Id, user.UserName!, user.Role, user.TenancyId);
	}

	/// <summary>
	/// Deletes a user account. GlobalAdmin can delete any user; TenantAdmin can
	/// delete users within their own tenancy only; TenantUser cannot delete accounts.
	/// </summary>
	/// <param name="id">Identity ID of the user to delete.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="NotFoundException">Thrown if the user does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller lacks permission.</exception>
	public async Task DeleteAsync(string id, CallerContext caller)
	{
		var user = await _userManager.FindByIdAsync(id)
			?? throw new NotFoundException();

		if (caller.IsTenantAdmin)
		{
			// TenantAdmin can only delete users within their own tenancy.
			if (user.TenancyId != caller.TenancyId)
			{
				throw new ForbiddenException();
			}
		}
		else if (caller.IsTenantUser)
		{
			// TenantUser cannot delete any account.
			throw new ForbiddenException();
		}

		// Write the audit entry before deletion so the UserId foreign key is still valid.
		_audit.Log(caller.UserId, caller.TenancyId, "UserDeleted", notes: $"Username={user.UserName}");
		await _db.SaveChangesAsync();

		// Identity's DeleteAsync handles hashed-password cleanup and any registered hooks.
		await _userManager.DeleteAsync(user);
	}

	/// <summary>
	/// Changes the password for any user. GlobalAdmin can reset any user's password;
	/// TenantAdmin can reset passwords within their tenancy; TenantUser can only
	/// reset their own password.
	/// </summary>
	/// <param name="id">Identity ID of the target user.</param>
	/// <param name="newPassword">The new password to set.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="NotFoundException">Thrown if the user does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller lacks permission to reset this user's password.</exception>
	/// <exception cref="IdentityOperationException">Thrown if the new password violates the configured policy.</exception>
	public async Task ChangePasswordAsync(string id, string newPassword, CallerContext caller)
	{
		var user = await _userManager.FindByIdAsync(id)
			?? throw new NotFoundException();

		if (caller.IsTenantAdmin)
		{
			// TenantAdmin can only reset passwords for users in their own tenancy.
			if (user.TenancyId != caller.TenancyId)
			{
				throw new ForbiddenException();
			}
		}
		else if (caller.IsTenantUser)
		{
			// TenantUser can only reset their own password.
			if (user.Id != caller.UserId)
			{
				throw new ForbiddenException();
			}
		}

		// Remove the existing password then add the new one.
		// This approach bypasses the old-password requirement because the caller
		// has already authenticated via Basic Auth on this request.
		await _userManager.RemovePasswordAsync(user);
		var result = await _userManager.AddPasswordAsync(user, newPassword);
		if (!result.Succeeded)
		{
			throw new IdentityOperationException(result.Errors.Select(e => e.Description));
		}
	}

	/// <summary>
	/// Changes the password of the caller's own account. Used by the self-service
	/// <c>PUT /api/auth/password</c> endpoint where no cross-user access check
	/// is needed — the caller can only ever target themselves.
	/// </summary>
	/// <param name="userId">Identity ID of the authenticated user changing their own password.</param>
	/// <param name="newPassword">The new password to set.</param>
	/// <exception cref="NotFoundException">Thrown if the user record cannot be found (should not happen for an authenticated user).</exception>
	/// <exception cref="IdentityOperationException">Thrown if the new password violates the configured policy.</exception>
	public async Task ChangeOwnPasswordAsync(string userId, string newPassword)
	{
		var user = await _userManager.FindByIdAsync(userId)
			?? throw new NotFoundException();

		await _userManager.RemovePasswordAsync(user);
		var result = await _userManager.AddPasswordAsync(user, newPassword);
		if (!result.Succeeded)
		{
			throw new IdentityOperationException(result.Errors.Select(e => e.Description));
		}
	}
}
