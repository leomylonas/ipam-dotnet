namespace IpamService.Models;

/// <summary>
/// Immutable record of every significant action taken in the system.
/// Written transactionally alongside the mutation it records (allocation,
/// release, subnet creation, etc.) so there is never a mutation without a
/// corresponding audit entry. Never updated or deleted — append-only.
/// </summary>
public class AuditLog
{
	/// <summary>The unique identifier for this audit entry.</summary>
	public Guid Id { get; set; }

	/// <summary>Identity ID of the user who performed the action.</summary>
	public string UserId { get; set; } = string.Empty;

	/// <summary>
	/// Tenancy context at the time of the action. Null for GlobalAdmin
	/// operations that are not scoped to a specific tenancy.
	/// </summary>
	public Guid? TenancyId { get; set; }

	/// <summary>
	/// Short action name describing what happened, e.g. <c>Allocated</c>,
	/// <c>Released</c>, <c>BulkAllocated</c>, <c>SubnetCreated</c>.
	/// </summary>
	public string Action { get; set; } = string.Empty;

	/// <summary>
	/// The IP address involved in the action, when applicable.
	/// Null for actions like SubnetCreated where no IP is the subject.
	/// </summary>
	public string? IpAddress { get; set; }

	/// <summary>
	/// The subnet involved in the action, when applicable.
	/// Null for actions that are not subnet-scoped.
	/// </summary>
	public Guid? SubnetId { get; set; }

	/// <summary>UTC timestamp when the action occurred.</summary>
	public DateTime Timestamp { get; set; }

	/// <summary>
	/// Optional extra context about the action, e.g. BulkId for bulk
	/// allocations or tenancy name for tenancy lifecycle events.
	/// </summary>
	public string? Notes { get; set; }
}
