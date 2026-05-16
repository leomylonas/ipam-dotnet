using System.Security.Claims;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Handles self-service authentication operations. Currently exposes a single
/// endpoint that lets any authenticated user change their own password. All
/// other credential management (admin-driven resets) is handled in
/// <see cref="UsersController"/>.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : ControllerBase
{
	/// <summary>Identity service used to look up users and update passwords.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>
	/// Initialises a new instance of <see cref="AuthController"/>.
	/// </summary>
	/// <param name="userManager">ASP.NET Identity user manager, injected by the DI container.</param>
	public AuthController(UserManager<ApplicationUser> userManager)
	{
		_userManager = userManager;
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
	public async Task<IActionResult> ChangeOwnPassword([FromBody] ChangePasswordRequest req)
	{
		// Resolve the caller's user ID from the claims set that BasicAuthHandler built.
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
		var user = await _userManager.FindByIdAsync(userId);

		// This should never be null for an authenticated user, but we handle it
		// defensively to avoid a null-reference exception.
		if (user is null)
		{
			return NotFound();
		}

		// Remove the existing password and add the new one in one logical step.
		// Using RemovePassword + AddPassword avoids the need to supply the old
		// password (the Basic Auth header already served as proof of identity).
		await _userManager.RemovePasswordAsync(user);
		var addResult = await _userManager.AddPasswordAsync(user, req.NewPassword);

		if (!addResult.Succeeded)
		{
			// Surface the Identity validation errors (e.g. "Passwords must have
			// at least one uppercase") so the caller knows what to fix.
			return BadRequest(addResult.Errors.Select(e => e.Description));
		}

		// RFC 7231: 204 is the standard success response for a mutation that
		// produces no new resource to return.
		return NoContent();
	}
}
