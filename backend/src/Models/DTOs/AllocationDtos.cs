namespace IpamService.Models.DTOs;

/// <summary>
/// Request body for POST /api/allocations (single allocation).
/// The system picks the next available IP automatically — the caller does not
/// specify which IP they want.
/// </summary>
/// <param name="SubnetId">The subnet to allocate an IP from.</param>
/// <param name="Description">Free-form description of what this IP will be used for.</param>
public record AllocateRequest(
	Guid SubnetId,
	string Description
);

/// <summary>
/// Request body for POST /api/allocations/bulk.
/// Requests <paramref name="Count"/> consecutive IPs from the specified subnet.
/// Returns 409 Conflict if no contiguous block of that size is available.
/// </summary>
/// <param name="SubnetId">The subnet to allocate from.</param>
/// <param name="Count">Number of consecutive IPs required. Must be positive.</param>
/// <param name="Description">Applied to all allocations in the bulk request.</param>
public record BulkAllocateRequest(
	Guid SubnetId,
	int Count,
	string Description
);

/// <summary>
/// Response shape returned by the IP availability check endpoint
/// <c>GET /api/subnets/{subnetId}/check/{ip}</c>.
/// </summary>
/// <param name="Ip">The IP address that was checked, in dotted-decimal format.</param>
/// <param name="Available">
/// <c>true</c> if the address is neither allocated nor within any exclusion range;
/// <c>false</c> otherwise.
/// </param>
public record CheckIpResponse(
	string Ip,
	bool Available
);

/// <summary>
/// Response shape returned when listing or creating allocations.
/// </summary>
/// <param name="Id">The allocation's unique identifier.</param>
/// <param name="IpAddress">The allocated IP address as a dotted-decimal string.</param>
/// <param name="UserId">Identity ID of the user who requested the allocation.</param>
/// <param name="SubnetId">Subnet the IP was drawn from.</param>
/// <param name="Description">Description provided at allocation time.</param>
/// <param name="AllocatedAt">UTC timestamp when the allocation was made.</param>
/// <param name="BulkId">Shared ID for all IPs allocated in the same bulk request; null for single allocations.</param>
public record AllocationResponse(
	Guid Id,
	string IpAddress,
	string UserId,
	Guid SubnetId,
	string Description,
	DateTime AllocatedAt,
	Guid? BulkId
);
