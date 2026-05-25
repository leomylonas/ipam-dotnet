namespace IpamService.Models;

/// <summary>
/// Records that a specific IP address has been allocated to a user within a subnet.
/// Each row represents one IP. Bulk allocations produce multiple rows that share a
/// common <see cref="BulkId"/> so they can be identified as a group, but each IP
/// is still individually releasable.
/// </summary>
public class Allocation
{
	/// <summary>The unique identifier for this allocation.</summary>
	public Guid Id { get; set; }

	/// <summary>
	/// The allocated IP address as a dotted-decimal string, e.g. <c>192.168.1.5</c>.
	/// Stored as a string so it works identically across all database providers.
	/// </summary>
	public string IpAddress { get; set; } = string.Empty;

	/// <summary>Identity ID of the user who requested this allocation.</summary>
	public string UserId { get; set; } = string.Empty;

	/// <summary>The subnet from which this IP was drawn.</summary>
	public Guid SubnetId { get; set; }

	/// <summary>Free-form description of what this IP is being used for.</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>UTC timestamp when this IP was allocated.</summary>
	public DateTime AllocatedAt { get; set; }

	/// <summary>
	/// Groups IPs that were allocated together in a single bulk request.
	/// Null for single allocations. All IPs in one bulk request share the
	/// same <see cref="BulkId"/> so callers can identify the batch.
	/// </summary>
	public Guid? BulkId { get; set; }
}
