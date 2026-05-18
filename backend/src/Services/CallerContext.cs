using IpamService.Models;

namespace IpamService.Services;

/// <summary>
/// Encapsulates the identity and tenancy context of the authenticated caller
/// for a single HTTP request. Service methods accept a <see cref="CallerContext"/>
/// so they can enforce role-based and tenancy-scoped access rules without
/// needing to reach back into <c>HttpContext</c> or the ASP.NET claims principal.
///
/// Constructed by the base controller helper <c>IpamControllerBase.GetCaller()</c>
/// from the claims placed into <c>HttpContext.User</c> by <c>BasicAuthHandler</c>.
/// </summary>
/// <param name="UserId">The ASP.NET Identity ID string of the authenticated user.</param>
/// <param name="Role">The role assigned to the user: <c>GlobalAdmin</c>, <c>TenantAdmin</c>, or <c>TenantUser</c>.</param>
/// <param name="TenancyId">The tenancy the user belongs to, or <c>null</c> for GlobalAdmin accounts.</param>
public record CallerContext(string UserId, string Role, Guid? TenancyId)
{
	/// <summary>
	/// Returns <c>true</c> when the caller holds the <c>GlobalAdmin</c> role,
	/// meaning they have unrestricted access to all system resources.
	/// </summary>
	public bool IsGlobalAdmin => Role == Roles.GlobalAdmin;

	/// <summary>
	/// Returns <c>true</c> when the caller holds the <c>TenantAdmin</c> role,
	/// meaning they can manage resources within their own tenancy only.
	/// </summary>
	public bool IsTenantAdmin => Role == Roles.TenantAdmin;

	/// <summary>
	/// Returns <c>true</c> when the caller holds the <c>TenantUser</c> role,
	/// meaning they have the most restricted access (allocations only).
	/// </summary>
	public bool IsTenantUser => Role == Roles.TenantUser;
}
