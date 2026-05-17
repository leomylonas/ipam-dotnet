namespace IpamService.Models;

/// <summary>
/// Defines the string constants used for the three IPAM roles. These constants
/// are the single source of truth for role names throughout the codebase:
/// they are used in <see cref="Services.CallerContext"/> property checks,
/// service-layer comparisons, <c>[Authorize(Roles = …)]</c> attributes, and
/// whenever a role is assigned to a new <see cref="ApplicationUser"/>.
///
/// <list type="bullet">
///   <item><description>
///     <see cref="GlobalAdmin"/> — unrestricted access to the entire system,
///     no tenancy affiliation.
///   </description></item>
///   <item><description>
///     <see cref="TenantAdmin"/> — manages resources within a single tenancy:
///     users, private subnets, exclusions, and audit visibility.
///   </description></item>
///   <item><description>
///     <see cref="TenantUser"/> — the most restricted role; can request and
///     release IP allocations and manage their own tags.
///   </description></item>
/// </list>
/// </summary>
public static class Roles
{
	/// <summary>
	/// The role name for a global administrator who has unrestricted access to
	/// all tenancies, subnets, allocations, and users. GlobalAdmin accounts
	/// carry no tenancy affiliation (<c>TenancyId</c> is <c>null</c>).
	/// </summary>
	public const string GlobalAdmin = "GlobalAdmin";

	/// <summary>
	/// The role name for a tenant-scoped administrator. TenantAdmin accounts
	/// can manage users, private subnets, exclusions, and view audit entries
	/// within their own tenancy only.
	/// </summary>
	public const string TenantAdmin = "TenantAdmin";

	/// <summary>
	/// The role name for an ordinary tenant user. TenantUser accounts can
	/// request and release IP allocations on subnets accessible to their
	/// tenancy, and manage tags on allocations they own.
	/// </summary>
	public const string TenantUser = "TenantUser";

	/// <summary>
	/// A comma-separated string combining <see cref="TenantAdmin"/> and
	/// <see cref="TenantUser"/>, suitable for use in
	/// <c>[Authorize(Roles = Roles.TenantMembers)]</c> where either
	/// tenant-scoped role is permitted.
	/// </summary>
	public const string TenantMembers = TenantAdmin + "," + TenantUser;
}
