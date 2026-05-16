namespace IpamService.Models.DTOs;

/// <summary>
/// Response shape for GET /api/subnets/{subnetId}/stats.
/// Provides a snapshot of IP utilisation within a subnet at the moment of
/// the request. The counts are computed live from the database, not cached.
/// Note: TotalIps excludes the network address and broadcast address — it
/// reflects usable host addresses only.
/// </summary>
/// <param name="SubnetId">The subnet these statistics relate to.</param>
/// <param name="TotalIps">Number of usable host IPs in the subnet (excludes network and broadcast).</param>
/// <param name="AllocatedCount">Number of IPs currently allocated.</param>
/// <param name="FreeCount">Number of IPs available for allocation (not allocated and not excluded).</param>
/// <param name="ExcludedCount">Number of IPs covered by exclusion rules.</param>
public record SubnetStatsResponse(
	Guid SubnetId,
	int TotalIps,
	int AllocatedCount,
	int FreeCount,
	int ExcludedCount
);
