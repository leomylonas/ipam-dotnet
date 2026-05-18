using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using IpamService.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IpamService.Auth;

/// <summary>
/// ASP.NET Core authentication handler that implements stateless HTTP Basic
/// Authentication. On every request it decodes the <c>Authorization: Basic</c>
/// header, validates the credentials against ASP.NET Identity, and, on
/// success, constructs a <see cref="ClaimsPrincipal"/> carrying the user's ID,
/// username, role, and optional tenancy ID.
///
/// Because this handler derives from <see cref="AuthenticationHandler{TOptions}"/>
/// it plugs directly into the middleware pipeline and is invoked automatically
/// by <c>UseAuthentication()</c>. No sessions, cookies, or tokens are issued —
/// each request is authenticated independently.
/// </summary>
public class BasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	/// <summary>Identity service for looking up users by username.</summary>
	private readonly UserManager<ApplicationUser> _userManager;

	/// <summary>Identity service used to validate passwords without side-effects.</summary>
	private readonly SignInManager<ApplicationUser> _signInManager;

	/// <summary>
	/// Initialises a new instance of <see cref="BasicAuthHandler"/>.
	/// </summary>
	/// <param name="options">Authentication scheme options monitor, injected by the framework.</param>
	/// <param name="logger">Logger factory used by the base class.</param>
	/// <param name="encoder">URL encoder used by the base class.</param>
	/// <param name="userManager">ASP.NET Identity user manager for credential lookup.</param>
	/// <param name="signInManager">ASP.NET Identity sign-in manager for password validation.</param>
	public BasicAuthHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger,
		UrlEncoder encoder,
		UserManager<ApplicationUser> userManager,
		SignInManager<ApplicationUser> signInManager)
		: base(options, logger, encoder)
	{
		_userManager = userManager;
		_signInManager = signInManager;
	}

	/// <summary>
	/// Core authentication logic called by the framework for every incoming request.
	/// Reads the <c>Authorization</c> header, decodes the Base64 credentials,
	/// looks up the user, and verifies the password.
	/// </summary>
	/// <returns>
	/// <see cref="AuthenticateResult.Success"/> with a populated ticket on valid credentials;
	/// <see cref="AuthenticateResult.NoResult"/> when no Authorization header is present;
	/// <see cref="AuthenticateResult.Fail"/> for malformed headers or wrong credentials.
	/// </returns>
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		// If there is no Authorization header at all the request is anonymous —
		// return NoResult so the pipeline can decide whether to allow or challenge it.
		if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
		{
			return AuthenticateResult.NoResult();
		}

		// Parse the header into scheme + parameter. We only handle Basic here.
		if (!AuthenticationHeaderValue.TryParse(authHeader, out var parsed) ||
			!string.Equals(parsed.Scheme, AuthConstants.Schemes.Basic, StringComparison.OrdinalIgnoreCase) ||
			parsed.Parameter is null)
		{
			return AuthenticateResult.Fail("Invalid Authorization header");
		}

		// The parameter is Base64-encoded "username:password". Decode it and
		// handle corrupt encoding gracefully rather than letting an exception
		// propagate unhandled.
		string credentials;
		try
		{
			credentials = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
		}
		catch
		{
			return AuthenticateResult.Fail("Invalid Base64 encoding");
		}

		// Split on the FIRST colon only. RFC 7617 allows colons in the password
		// part, so we must not split on every colon.
		var separatorIndex = credentials.IndexOf(':');
		if (separatorIndex < 0)
		{
			return AuthenticateResult.Fail("Invalid credentials format");
		}

		// Extract the username and password from either side of the separator.
		var username = credentials[..separatorIndex];
		var password = credentials[(separatorIndex + 1)..];

		// Look up the user by username. A null return means the user does not
		// exist; we return the same failure message to avoid username enumeration.
		var user = await _userManager.FindByNameAsync(username);
		if (user is null)
		{
			return AuthenticateResult.Fail("Invalid credentials");
		}

		// Validate the password without creating a sign-in session or updating
		// the lockout counter (lockoutOnFailure: false keeps Basic auth stateless).
		var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
		if (!result.Succeeded)
		{
			return AuthenticateResult.Fail("Invalid credentials");
		}

		// Build the standard set of claims. The role claim drives [Authorize(Roles=...)]
		// policy enforcement throughout the rest of the application.
		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id),
			new(ClaimTypes.Name, user.UserName!),
			new(ClaimTypes.Role, user.Role),
		};

		// TenancyId is nullable — GlobalAdmin has no tenancy affiliation and
		// therefore no tenancy claim. Controllers that need the tenancy ID
		// check for the claim explicitly.
		if (user.TenancyId.HasValue)
		{
			claims.Add(new Claim(AuthConstants.Claims.TenancyId, user.TenancyId.Value.ToString()));
		}

		// Wrap claims in an identity and principal, then package into a ticket
		// that the framework stores on HttpContext.User.
		var identity = new ClaimsIdentity(claims, Scheme.Name);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, Scheme.Name);

		return AuthenticateResult.Success(ticket);
	}

	/// <summary>
	/// Called by the framework when a request requires authentication but none
	/// was provided (or authentication failed). Sets the standard
	/// <c>WWW-Authenticate: Basic</c> response header and returns HTTP 401.
	/// </summary>
	/// <param name="properties">Authentication properties provided by the framework (unused here).</param>
	/// <returns>A completed task; the response is written synchronously.</returns>
	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		// Tell the client which authentication scheme to use on the next request.
		Response.Headers.WWWAuthenticate = AuthConstants.Schemes.Basic;
		Response.StatusCode = 401;
		return Task.CompletedTask;
	}
}
