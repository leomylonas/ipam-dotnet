using System.Security.Claims;
using IpamService.Auth;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Handles cookie-based session management for the React UI. The three
/// endpoints here are companions to the existing stateless Basic Auth scheme:
/// <list type="bullet">
///   <item><term>POST /auth/login</term><description>Validates JSON credentials and issues an encrypted ASP.NET Core cookie.</description></item>
///   <item><term>POST /auth/logout</term><description>Clears the cookie, ending the UI session.</description></item>
///   <item><term>GET /auth/me</term><description>Returns the current user's profile so the UI router can initialise role-based navigation on page load.</description></item>
/// </list>
///
/// Direct API consumers using Basic Auth on every request are unaffected by
/// this controller — they never need to interact with it.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthController : IpamControllerBase
{
	/// <summary>Identity service for looking up users by username.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>Identity service used to validate passwords without side-effects.</summary>
	private readonly SignInManager<ApplicationUser> _signInManager;

	/// <summary>
	/// Initialises a new instance of <see cref="AuthController"/>.
	/// </summary>
	/// <param name="userManager">Identity user manager, injected by the DI container.</param>
	/// <param name="signInManager">Identity sign-in manager, injected by the DI container.</param>
	public AuthController(
		UserManager<ApplicationUser> userManager,
		SignInManager<ApplicationUser> signInManager)
	{
		_userManager = userManager;
		_signInManager = signInManager;
	}

	/// <summary>
	/// Authenticates the user with the supplied credentials and, on success,
	/// issues an encrypted ASP.NET Core cookie that the browser will include on
	/// all subsequent requests to the same origin.
	///
	/// The cookie is HttpOnly, SameSite=Strict, and encrypted using ASP.NET
	/// Core Data Protection — it cannot be read or forged by client-side code.
	/// </summary>
	/// <param name="req">JSON body containing <c>username</c> and <c>password</c>.</param>
	/// <returns>
	/// <c>200 OK</c> with an <see cref="AuthMeResponse"/> carrying the user's profile
	/// (ID, username, role, tenancyId) so the UI can initialise routing immediately;
	/// <c>401 Unauthorized</c> if credentials are invalid (no detail to prevent enumeration).
	/// </returns>
	[HttpPost("login")]
	[AllowAnonymous]
	public async Task<IActionResult> Login([FromBody] LoginRequest req)
	{
		// Look up the user. Return 401 with no body on any failure — never reveal
		// whether the username exists vs. the password being wrong.
		var user = await _userManager.FindByNameAsync(req.Username);
		if (user is null)
		{
			return Unauthorized();
		}

		// Validate the password without updating lockout counters (lockoutOnFailure: false)
		// because Basic Auth elsewhere in the system already has no lockout policy.
		var result = await _signInManager.CheckPasswordSignInAsync(
			user, req.Password, lockoutOnFailure: false);

		if (!result.Succeeded)
		{
			return Unauthorized();
		}

		// Build the same set of claims that BasicAuthHandler produces so that
		// cookie-authenticated requests are indistinguishable from Basic-authenticated
		// ones from the perspective of controllers and services.
		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id),
			new(ClaimTypes.Name, user.UserName!),
			new(ClaimTypes.Role, user.Role),
		};

		// TenancyId is nullable — GlobalAdmin has no tenancy affiliation.
		if (user.TenancyId.HasValue)
		{
			claims.Add(new Claim(AuthConstants.Claims.TenancyId, user.TenancyId.Value.ToString()));
		}

		var identity = new ClaimsIdentity(claims, AuthConstants.Schemes.Cookie);
		var principal = new ClaimsPrincipal(identity);

		// Issue the encrypted cookie. The scheme name must match the one
		// registered in AddCookie() in Program.cs.
		await HttpContext.SignInAsync(AuthConstants.Schemes.Cookie, principal);

		return Ok(new AuthMeResponse(user.Id, user.UserName!, user.Role, user.TenancyId));
	}

	/// <summary>
	/// Ends the current UI session by clearing the authentication cookie.
	/// Requires an authenticated caller — either cookie or Basic Auth — so that
	/// unauthenticated requests cannot spam the logout endpoint.
	/// </summary>
	/// <returns><c>204 No Content</c> unconditionally once authenticated.</returns>
	[HttpPost("logout")]
	[Authorize]
	public async Task<IActionResult> Logout()
	{
		// SignOutAsync clears the cookie. If the caller authenticated via Basic Auth
		// (no cookie present), this is a no-op — the call still succeeds.
		await HttpContext.SignOutAsync(AuthConstants.Schemes.Cookie);
		return NoContent();
	}

	/// <summary>
	/// Returns the authenticated caller's profile. The React UI calls this
	/// endpoint on page load to check whether an existing cookie is still valid
	/// and to retrieve the role information needed to initialise routing.
	/// </summary>
	/// <returns>
	/// <c>200 OK</c> with an <see cref="AuthMeResponse"/> if the caller is authenticated;
	/// <c>401 Unauthorized</c> if no valid cookie or Basic Auth header is present.
	/// </returns>
	[HttpGet("me")]
	[Authorize]
	public async Task<IActionResult> Me()
	{
		// GetCaller() reads from HttpContext.User which was populated by whichever
		// auth scheme handled this request (Basic or Cookie).
		var caller = GetCaller();

		var user = await _userManager.FindByIdAsync(caller.UserId);
		if (user is null)
		{
			// Should never happen — the user was authenticated moments ago —
			// but guard gracefully in case of a concurrent delete.
			return Unauthorized();
		}

		return Ok(new AuthMeResponse(user.Id, user.UserName!, user.Role, user.TenancyId));
	}
}
