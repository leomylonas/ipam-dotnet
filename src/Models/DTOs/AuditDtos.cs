namespace IpamService.Models.DTOs;

/// <summary>
/// Response shape for GET /api/audit entries.
/// Audit logs are read-only; they cannot be modified or deleted through the API.
/// Results are returned newest-first.
/// </summary>
/// <param name="Id">The audit entry's unique identifier.</param>
/// <param name="UserId">Identity ID of the user who performed the action.</param>
/// <param name="TenancyId">Tenancy context at the time of the action, or null for GlobalAdmin actions.</param>
/// <param name="Action">Short verb describing what happened, e.g. Allocated, Released, SubnetCreated.</param>
/// <param name="IpAddress">IP address involved in the action, or null when not applicable.</param>
/// <param name="SubnetId">Subnet involved in the action, or null when not applicable.</param>
/// <param name="Timestamp">UTC timestamp when the action occurred.</param>
/// <param name="Notes">Optional extra context about the action.</param>
public record AuditLogResponse(
	Guid Id,
	string UserId,
	Guid? TenancyId,
	string Action,
	string? IpAddress,
	Guid? SubnetId,
	DateTime Timestamp,
	string? Notes
);
