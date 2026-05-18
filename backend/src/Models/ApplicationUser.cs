using Microsoft.AspNetCore.Identity;

namespace IpamService.Models;

/// <summary>
/// Extends the default ASP.NET Identity user with IPAM-specific properties.
/// Every user belongs to exactly one role and, except for GlobalAdmin, to
/// exactly one tenancy.
/// </summary>
public class ApplicationUser : IdentityUser
{
	/// <summary>
	/// The tenancy this user belongs to. Null for GlobalAdmin users, who operate
	/// across all tenancies and do not have a single-tenancy affiliation.
	/// </summary>
	public Guid? TenancyId { get; set; }

	/// <summary>
	/// The role assigned to this user. One of: <c>GlobalAdmin</c>,
	/// <c>TenantAdmin</c>, or <c>TenantUser</c>. Stored as a plain string
	/// rather than via Identity roles to keep the model simple.
	/// </summary>
	public string Role { get; set; } = string.Empty;
}
