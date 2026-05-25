using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Manages user account operations: listing, creation, update, and deletion.
/// Role-based authorization rules are enforced here so that permission logic is
/// co-located with the data access it governs rather than scattered across
/// controller action methods.
///
/// Authorization summary:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Unrestricted access to all user operations across all tenancies.</description></item>
///   <item><term>TenantAdmin</term><description>Can only list and manage <c>TenantUser</c> accounts within their own tenancy.</description></item>
///   <item><term>TenantUser</term><description>Can only update their own account, and only the password field.</description></item>
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
			throw new IdentityOperationException(result.Errors);
		}

		_audit.Log(caller.UserId, caller.TenancyId, "UserCreated",
			notes: $"Username={req.Username},Role={req.Role}");
		await _db.SaveChangesAsync();

		return new UserResponse(user.Id, user.UserName!, user.Role, user.TenancyId);
	}

	/// <summary>
	/// Updates a user's profile fields and, when <see cref="UpdateUserRequest.Password"/>
	/// is supplied, changes their password in the same operation.
	///
	/// Permission rules:
	/// <list type="bullet">
	///   <item><term>GlobalAdmin</term><description>No restrictions — can update any user's profile and password.</description></item>
	///   <item><term>TenantAdmin</term><description>Can only update TenantUser accounts within their own tenancy. Cannot escalate roles.</description></item>
	///   <item><term>TenantUser</term><description>Can only call this endpoint for their own user ID, and only to change their password. All profile fields are ignored.</description></item>
	/// </list>
	/// </summary>
	/// <param name="id">Identity ID of the user to update.</param>
	/// <param name="req">Request body containing the new profile values and an optional new password.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The updated user as a <see cref="UserResponse"/>.</returns>
	/// <exception cref="NotFoundException">Thrown if the user does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller lacks permission for the requested change.</exception>
	/// <exception cref="IdentityOperationException">Thrown if Identity rejects the username update or the new password.</exception>
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
			// TenantUser can only update their own password — no profile field changes.
			if (user.Id != caller.UserId)
			{
				throw new ForbiddenException();
			}

			// If the caller is TenantUser and no password is provided, there is
			// nothing to do — they cannot change any other field.
			if (req.Password is null)
			{
				return new UserResponse(user.Id, user.UserName!, user.Role, user.TenancyId);
			}

			// Apply password change only — skip the profile update block below.
			await ApplyPasswordChangeAsync(user, req.Password);
			return new UserResponse(user.Id, user.UserName!, user.Role, user.TenancyId);
		}

		// GlobalAdmin / TenantAdmin path: update profile fields.
		user.UserName = req.Username;
		user.Email = req.Username;
		user.Role = req.Role;
		user.TenancyId = req.TenancyId;

		var result = await _userManager.UpdateAsync(user);
		if (!result.Succeeded)
		{
			throw new IdentityOperationException(result.Errors);
		}

		// If a new password was also supplied, apply it after the profile update.
		if (req.Password is not null)
		{
			await ApplyPasswordChangeAsync(user, req.Password);
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

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Replaces a user's password by removing the existing one and adding the new
	/// one. This bypass-approach is intentional — the caller has already been
	/// authenticated by the request pipeline, so the old password is not required.
	/// </summary>
	/// <param name="user">The user whose password to change.</param>
	/// <param name="newPassword">The new password string. Must pass the configured policy.</param>
	/// <exception cref="IdentityOperationException">Thrown if the new password violates the configured policy.</exception>
	private async Task ApplyPasswordChangeAsync(ApplicationUser user, string newPassword)
	{
		// Remove the existing password hash then add the new one.
		// Using Remove+Add instead of ResetPasswordAsync avoids requiring a
		// password-reset token, which is appropriate here because authorization
		// has already been verified at the service boundary.
		await _userManager.RemovePasswordAsync(user);
		var result = await _userManager.AddPasswordAsync(user, newPassword);
		if (!result.Succeeded)
		{
			throw new IdentityOperationException(result.Errors);
		}
	}
}
