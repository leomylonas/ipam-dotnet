namespace IpamService.Models.DTOs;

/// <summary>
/// Request body used when creating either a shared or a private subnet.
/// The Type and TenancyId are inferred from the endpoint being called, not
/// the request body, so they are not included here.
/// </summary>
/// <param name="Cidr">CIDR notation, e.g. <c>192.168.1.0/24</c>. Must be valid and non-overlapping.</param>
/// <param name="Name">Human-readable label for the subnet.</param>
/// <param name="Description">Free-form description of the subnet's purpose.</param>
public record CreateSubnetRequest(
	string Cidr,
	string Name,
	string Description
);

/// <summary>
/// Response shape returned when listing or creating subnets.
/// </summary>
/// <param name="Id">The subnet's unique identifier.</param>
/// <param name="Cidr">CIDR notation for the subnet.</param>
/// <param name="Name">Human-readable label.</param>
/// <param name="Description">Free-form description.</param>
/// <param name="Type">Either <c>Shared</c> or <c>Private</c>.</param>
/// <param name="TenancyId">Owning tenancy for Private subnets; null for Shared.</param>
/// <param name="CreatedAt">UTC timestamp when the subnet was created.</param>
public record SubnetResponse(
	Guid Id,
	string Cidr,
	string Name,
	string Description,
	string Type,
	Guid? TenancyId,
	DateTime CreatedAt
);

/// <summary>
/// Request body for POST /api/subnets/shared/{id}/access.
/// Adds a tenancy restriction to a shared subnet so that only the listed
/// tenancies may allocate from it.
/// </summary>
/// <param name="TenancyId">The tenancy to grant access to.</param>
public record GrantSubnetAccessRequest(
	Guid TenancyId
);
