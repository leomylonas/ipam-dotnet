namespace IpamService.Models.DTOs;

/// <summary>
/// Request body for POST /api/tenancies.
/// Creating a tenancy also creates the first TenantAdmin user in one atomic
/// operation, so the admin credentials are included here.
/// </summary>
/// <param name="Name">Unique display name for the new tenancy.</param>
/// <param name="Description">Free-form description of the tenancy.</param>
/// <param name="AdminUsername">Username for the initial TenantAdmin account.</param>
/// <param name="AdminPassword">Password for the initial TenantAdmin account. Must meet password policy.</param>
public record CreateTenancyRequest(
	string Name,
	string Description,
	string AdminUsername,
	string AdminPassword
);

/// <summary>
/// Response shape returned when listing or creating tenancies.
/// </summary>
/// <param name="Id">The tenancy's unique identifier.</param>
/// <param name="Name">The tenancy's unique display name.</param>
/// <param name="Description">Free-form description.</param>
/// <param name="CreatedAt">UTC timestamp when the tenancy was created.</param>
public record TenancyResponse(
	Guid Id,
	string Name,
	string Description,
	DateTime CreatedAt
);
