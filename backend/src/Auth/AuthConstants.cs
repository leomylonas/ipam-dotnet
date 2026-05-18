namespace IpamService.Auth;

/// <summary>
/// Central location for authentication-related string constants. Defining them
/// here means the strings are written once and referenced everywhere by name,
/// eliminating typo-prone magic strings scattered across handlers, controllers,
/// and startup code.
/// </summary>
public static class AuthConstants
{
	/// <summary>
	/// Names for the authentication schemes registered in <c>Program.cs</c>.
	/// These strings must match the names passed to <c>AddScheme</c>,
	/// <c>AddCookie</c>, and <c>AddPolicyScheme</c> exactly.
	/// </summary>
	public static class Schemes
	{
		/// <summary>
		/// Stateless HTTP Basic Auth scheme for direct API consumers.
		/// Registered via <c>AddScheme&lt;AuthenticationSchemeOptions, BasicAuthHandler&gt;</c>.
		/// </summary>
		public const string Basic = "Basic";

		/// <summary>
		/// Encrypted ASP.NET Core cookie scheme for the React UI.
		/// Registered via <c>AddCookie</c>. Issued by <c>POST /auth/login</c>.
		/// </summary>
		public const string Cookie = "Cookie";

		/// <summary>
		/// Policy scheme that forwards to <see cref="Basic"/> when an
		/// <c>Authorization</c> header is present, and to <see cref="Cookie"/>
		/// otherwise. Set as the default authenticate scheme so all
		/// <c>[Authorize]</c> endpoints accept either mechanism transparently.
		/// </summary>
		public const string Combined = "Combined";
	}

	/// <summary>
	/// Custom claim type names used in addition to the standard
	/// <see cref="System.Security.Claims.ClaimTypes"/> values.
	/// </summary>
	public static class Claims
	{
		/// <summary>
		/// Claim carrying the tenancy ID (a <see cref="System.Guid"/> string) for
		/// tenant-affiliated users. This claim is absent for GlobalAdmin accounts
		/// which have no tenancy affiliation.
		/// </summary>
		public const string TenancyId = "TenancyId";
	}

	/// <summary>
	/// Names and identifiers for the cookies managed by this application.
	/// </summary>
	public static class Cookies
	{
		/// <summary>
		/// The name of the authentication cookie issued by <c>POST /auth/login</c>
		/// and cleared by <c>POST /auth/logout</c>. Visible in browser dev-tools
		/// under this name; has no functional effect on encryption or validation.
		/// </summary>
		public const string AuthCookieName = "ipam_auth";
	}
}
