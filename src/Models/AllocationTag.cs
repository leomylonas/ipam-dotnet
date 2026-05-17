namespace IpamService.Models;

/// <summary>
/// A freeform key-value tag attached to an <see cref="Allocation"/>.
/// Tags let callers annotate IPs with arbitrary metadata (e.g. environment,
/// service name, ticket number). Keys must be unique per allocation; a
/// PUT to the tags endpoint does a full replace — all existing tags are
/// deleted and the new set is inserted atomically.
/// </summary>
public class AllocationTag
{
	/// <summary>The unique identifier for this tag row.</summary>
	public Guid Id { get; set; }

	/// <summary>The allocation this tag belongs to.</summary>
	public Guid AllocationId { get; set; }

	/// <summary>
	/// The tag key. Must be unique within the allocation — enforced by a
	/// composite unique index on (AllocationId, Key).
	/// </summary>
	public string Key { get; set; } = string.Empty;

	/// <summary>The tag value. No uniqueness constraint on values.</summary>
	public string Value { get; set; } = string.Empty;
}
