namespace IpamService.Models;

/// <summary>
/// Represents an isolated tenancy (organisation) within the IPAM system.
/// Each tenancy owns its own private subnets and users, completely separated
/// from every other tenancy.
/// </summary>
public class Tenancy
{
	/// <summary>The unique identifier for this tenancy.</summary>
	public Guid Id { get; set; }

	/// <summary>
	/// Human-readable name for the tenancy. Must be unique across all tenancies
	/// and is enforced with a database-level unique index.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Free-form description of the tenancy's purpose or ownership.</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>
	/// UTC timestamp at which this tenancy was created. Always stored and
	/// returned as UTC; never use local time.
	/// </summary>
	public DateTime CreatedAt { get; set; }
}
