namespace IpamService.Models.DTOs;

/// <summary>
/// Request body for POST /api/subnets/{subnetId}/exclusions.
/// Both Start and End are inclusive. For a single excluded IP, set both to the
/// same address. The allocation engine will never hand out any IP within this range.
/// </summary>
/// <param name="Start">First IP address in the excluded range (inclusive).</param>
/// <param name="End">Last IP address in the excluded range (inclusive). Equal to Start for single-IP exclusions.</param>
/// <param name="Description">Human-readable reason for the exclusion, e.g. "Gateway IP".</param>
public record CreateExclusionRequest(
	string Start,
	string End,
	string Description
);

/// <summary>
/// Request body for PUT /api/subnets/{subnetId}/exclusions/{id}. Range bounds
/// remain immutable; only the description is editable.
/// </summary>
/// <param name="Description">Human-readable reason for the exclusion.</param>
public record UpdateExclusionRequest(
	string Description
);

/// <summary>
/// Response shape returned when listing or creating exclusions.
/// </summary>
/// <param name="Id">The exclusion's unique identifier.</param>
/// <param name="SubnetId">The subnet this exclusion belongs to.</param>
/// <param name="Start">First IP in the excluded range.</param>
/// <param name="End">Last IP in the excluded range.</param>
/// <param name="Description">Reason for the exclusion.</param>
public record ExclusionResponse(
	Guid Id,
	Guid SubnetId,
	string Start,
	string End,
	string Description
);
