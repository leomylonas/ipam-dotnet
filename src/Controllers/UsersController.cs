using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Manages user accounts within the IPAM system. Access rules differ by role:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Can list and manage users across all tenancies and assign any role.</description></item>
///   <item><term>TenantAdmin</term><description>Can list and manage users within their own tenancy; can only create <c>TenantUser</c> accounts.</description></item>
///   <item><term>TenantUser</term><description>Cannot list or create users; can only change their own password via <see cref="AuthController"/>.</description></item>
/// </list>
///
/// All business logic and role-based permission checks are delegated to
/// <see cref="UserService"/>, keeping this controller thin and focused on
/// HTTP concerns only.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : IpamControllerBase
{
	/// <summary>Service that owns all user management business logic.</summary>
	private readonly UserService _users;

	/// <summary>
	/// Initialises a new instance of <see cref="UsersController"/>.
	/// </summary>
	/// <param name="users">User service, injected by the DI container.</param>
	public UsersController(UserService users)
	{
		_users = users;
	}

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
	public Task<IActionResult> List() =>
		ExecuteAsync(async () => Ok(await _users.ListAsync(GetCaller())));

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
	public Task<IActionResult> Create([FromBody] CreateUserRequest req) =>
		ExecuteAsync(async () =>
		{
			var result = await _users.CreateAsync(req, GetCaller());
			return CreatedAtAction(nameof(List), result);
		});

	/// <summary>
	/// Updates mutable profile fields for an existing user. Password changes are
	/// handled by dedicated password endpoints.
	/// </summary>
	/// <param name="id">The Identity user ID to update.</param>
	/// <param name="req">Request body with updated username, role, and tenancy.</param>
	/// <returns>
	/// <c>200 OK</c> with updated user details on success;
	/// <c>404 Not Found</c> if the user does not exist;
	/// <c>403 Forbidden</c> if the caller lacks permission for the requested change;
	/// <c>400 Bad Request</c> if Identity rejects the username update.
	/// </returns>
	[HttpPut("{id}")]
	public Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest req) =>
		ExecuteAsync(async () => Ok(await _users.UpdateAsync(id, req, GetCaller())));

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
	public Task<IActionResult> Delete(string id) =>
		ExecuteAsync(async () =>
		{
			await _users.DeleteAsync(id, GetCaller());
			return NoContent();
		});

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
	public Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordRequest req) =>
		ExecuteAsync(async () =>
		{
			await _users.ChangePasswordAsync(id, req.NewPassword, GetCaller());
			return NoContent();
		});
}
