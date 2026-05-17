using System.Security.Claims;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Handles self-service authentication operations. Currently exposes a single
/// endpoint that lets any authenticated user change their own password. All
/// other credential management (admin-driven resets) is handled in
/// <see cref="UsersController"/>.
///
/// Business logic is delegated to <see cref="UserService.ChangeOwnPasswordAsync"/>.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : IpamControllerBase
{
	/// <summary>Service that owns password management logic.</summary>
	private readonly UserService _users;

	/// <summary>
	/// Initialises a new instance of <see cref="AuthController"/>.
	/// </summary>
	/// <param name="users">User service, injected by the DI container.</param>
	public AuthController(UserService users)
	{
		_users = users;
	}

	/// <summary>
	/// Changes the authenticated user's own password.
	/// The old password is not required because the caller has already
	/// proved their identity via Basic Auth on this very request.
	/// </summary>
	/// <param name="req">Request body containing the desired new password.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the authenticated user cannot be resolved (should not happen);
	/// <c>400 Bad Request</c> if the new password violates the configured policy.
	/// </returns>
	[HttpPut("password")]
	public Task<IActionResult> ChangeOwnPassword([FromBody] ChangePasswordRequest req) =>
		ExecuteAsync(async () =>
		{
			// Resolve the caller's user ID from the claims built by BasicAuthHandler.
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
			await _users.ChangeOwnPasswordAsync(userId, req.NewPassword);
			return NoContent();
		});
}
