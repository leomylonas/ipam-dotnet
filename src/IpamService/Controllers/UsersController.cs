using System.Security.Claims;
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
/// Manages user accounts within the IPAM system. Access rules differ by role:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Can list and manage users across all tenancies and assign any role.</description></item>
///   <item><term>TenantAdmin</term><description>Can list and manage users within their own tenancy; can only create <c>TenantUser</c> accounts.</description></item>
///   <item><term>TenantUser</term><description>Cannot list or create users; can only change their own password via <see cref="AuthController"/>.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
	/// <summary>EF Core context for user queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Identity service for user creation, deletion, and password management.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>Audit service for recording user lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="UsersController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="userManager">Identity user manager, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public UsersController(AppDbContext db, UserManager<ApplicationUser> userManager, AuditService audit)
	{
		_db = db;
		_userManager = userManager;
		_audit = audit;
	}

	/// <summary>The identity ID of the currently authenticated user.</summary>
	private string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

	/// <summary>The role of the currently authenticated user (GlobalAdmin | TenantAdmin | TenantUser).</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>
	/// The tenancy ID of the currently authenticated user, or <c>null</c> for GlobalAdmin.
	/// </summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Returns the list of users visible to the caller.
	/// GlobalAdmin sees all users; TenantAdmin sees only users within their tenancy.
	/// TenantUser receives 403.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="UserResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller is a TenantUser.
	/// </returns>
	[HttpGet]
	public async Task<IActionResult> List()
	{
		if (CallerRole == "GlobalAdmin")
		{
			// GlobalAdmin can see every user regardless of tenancy.
			var all = await _db.Users
				.OfType<ApplicationUser>()
				.Select(u => new UserResponse(u.Id, u.UserName!, u.Role, u.TenancyId))
				.ToListAsync();
			return Ok(all);
		}

		if (CallerRole == "TenantAdmin")
		{
			// TenantAdmin can only see users within their own tenancy.
			var tenancyUsers = await _db.Users
				.OfType<ApplicationUser>()
				.Where(u => u.TenancyId == CallerTenancyId)
				.Select(u => new UserResponse(u.Id, u.UserName!, u.Role, u.TenancyId))
				.ToListAsync();
			return Ok(tenancyUsers);
		}

		// TenantUser has no visibility into user listings.
		return Forbid();
	}

	/// <summary>
	/// Creates a new user account. GlobalAdmin can create any role in any tenancy.
	/// TenantAdmin can only create TenantUser accounts within their own tenancy.
	/// </summary>
	/// <param name="req">Request body with username, password, role, and tenancy.</param>
	/// <returns>
	/// <c>201 Created</c> with the new user on success;
	/// <c>403 Forbidden</c> if the caller lacks permission for the requested role or tenancy;
	/// <c>400 Bad Request</c> if Identity rejects the password or username.
	/// </returns>
	[HttpPost]
	public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
	{
		if (CallerRole == "GlobalAdmin")
		{
			// GlobalAdmin can create any role in any tenancy — no additional checks needed.
		}
		else if (CallerRole == "TenantAdmin")
		{
			// TenantAdmin is restricted to creating TenantUser accounts only,
			// and only within their own tenancy.
			if (req.Role != "TenantUser")
			{
				return Forbid();
			}

			if (req.TenancyId != CallerTenancyId)
			{
				return Forbid();
			}
		}
		else
		{
			// TenantUser cannot create accounts.
			return Forbid();
		}

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
			return BadRequest(result.Errors.Select(e => e.Description));
		}

		_audit.Log(CallerId, CallerTenancyId, "UserCreated", notes: $"Username={req.Username},Role={req.Role}");
		await _db.SaveChangesAsync();

		return CreatedAtAction(nameof(List),
			new UserResponse(user.Id, user.UserName!, user.Role, user.TenancyId));
	}

	/// <summary>
	/// Deletes a user account. GlobalAdmin can delete any user; TenantAdmin can
	/// delete users within their own tenancy only.
	/// </summary>
	/// <param name="id">The Identity user ID of the user to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the user does not exist;
	/// <c>403 Forbidden</c> if the caller lacks permission.
	/// </returns>
	[HttpDelete("{id}")]
	public async Task<IActionResult> Delete(string id)
	{
		var user = await _userManager.FindByIdAsync(id);
		if (user is null)
		{
			return NotFound();
		}

		if (CallerRole == "GlobalAdmin")
		{
			// GlobalAdmin can delete any user — no further checks.
		}
		else if (CallerRole == "TenantAdmin")
		{
			// TenantAdmin can only delete users within their own tenancy.
			if (user.TenancyId != CallerTenancyId)
			{
				return Forbid();
			}
		}
		else
		{
			// TenantUser cannot delete accounts.
			return Forbid();
		}

		// Write the audit entry before deleting so the UserId is still resolvable.
		_audit.Log(CallerId, CallerTenancyId, "UserDeleted", notes: $"Username={user.UserName}");
		await _db.SaveChangesAsync();

		// Identity's DeleteAsync handles hashed-password cleanup and any
		// registered user-deletion hooks.
		await _userManager.DeleteAsync(user);
		return NoContent();
	}

	/// <summary>
	/// Changes the password of any user. GlobalAdmin can target any user;
	/// TenantAdmin can target users within their tenancy; TenantUser can only
	/// change their own password (use <c>PUT /api/auth/password</c> for convenience).
	/// </summary>
	/// <param name="id">The Identity user ID of the target user.</param>
	/// <param name="req">Request body containing the new password.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the user does not exist;
	/// <c>403 Forbidden</c> if the caller lacks permission;
	/// <c>400 Bad Request</c> if the new password fails Identity validation.
	/// </returns>
	[HttpPut("{id}/password")]
	public async Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordRequest req)
	{
		var user = await _userManager.FindByIdAsync(id);
		if (user is null)
		{
			return NotFound();
		}

		if (CallerRole == "GlobalAdmin")
		{
			// GlobalAdmin can reset any user's password.
		}
		else if (CallerRole == "TenantAdmin")
		{
			// TenantAdmin can reset passwords within their own tenancy.
			if (user.TenancyId != CallerTenancyId)
			{
				return Forbid();
			}
		}
		else
		{
			// TenantUser can only reset their own password.
			if (user.Id != CallerId)
			{
				return Forbid();
			}
		}

		// Remove + add is the recommended approach when bypassing the old-password
		// requirement (the caller already authenticated via Basic Auth).
		await _userManager.RemovePasswordAsync(user);
		var result = await _userManager.AddPasswordAsync(user, req.NewPassword);
		if (!result.Succeeded)
		{
			return BadRequest(result.Errors.Select(e => e.Description));
		}

		return NoContent();
	}
}
