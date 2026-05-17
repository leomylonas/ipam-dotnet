namespace IpamService.Models;

/// <summary>
/// Distinguishes between globally-shared subnets (managed by GlobalAdmin) and
/// private subnets that belong to a specific tenancy.
/// </summary>
public enum SubnetType
{
	/// <summary>
	/// Available to all tenancies by default, or restricted to a subset via
	/// <see cref="SubnetTenancyAccess"/> rows.
	/// </summary>
	Shared,

	/// <summary>
	/// Belongs exclusively to one tenancy. Must fall within RFC1918 ranges.
	/// </summary>
	Private
}

/// <summary>
/// Represents an IP subnet (CIDR block) that can be allocated from.
/// Subnets are either globally shared or private to a specific tenancy.
/// </summary>
public class Subnet
{
	/// <summary>The unique identifier for this subnet.</summary>
	public Guid Id { get; set; }

	/// <summary>
	/// CIDR notation for the subnet, e.g. <c>192.168.1.0/24</c>.
	/// Validated on creation; private subnets must be RFC1918.
	/// </summary>
	public string Cidr { get; set; } = string.Empty;

	/// <summary>Human-readable label for this subnet.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Free-form description of what this subnet is used for.</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>
	/// Whether this subnet is shared across tenancies or private to one.
	/// Determines access-control rules and which validation rules apply.
	/// </summary>
	public SubnetType Type { get; set; }

	/// <summary>
	/// Owning tenancy for Private subnets. Null for Shared subnets, which have
	/// no single owner — they are governed by GlobalAdmin.
	/// </summary>
	public Guid? TenancyId { get; set; }

	/// <summary>UTC timestamp when this subnet was registered in the system.</summary>
	public DateTime CreatedAt { get; set; }
}
