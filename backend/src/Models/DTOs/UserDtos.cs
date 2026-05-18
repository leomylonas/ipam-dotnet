namespace IpamService.Models.DTOs;

/// <summary>
/// Request body for POST /api/users.
/// GlobalAdmin can set any role and any tenancy; TenantAdmin is restricted
/// to creating TenantUser accounts within their own tenancy.
/// </summary>
/// <param name="Username">Login username for the new user.</param>
/// <param name="Password">Initial password. Must meet password policy.</param>
/// <param name="Role">One of: GlobalAdmin, TenantAdmin, TenantUser.</param>
/// <param name="TenancyId">Tenancy the user belongs to. Null for GlobalAdmin.</param>
public record CreateUserRequest(
	string Username,
	string Password,
	string Role,
	Guid? TenancyId
);

/// <summary>
/// Request body for PUT /api/users/{id}. Updates profile fields and, when
/// <see cref="Password"/> is supplied, changes the password in the same request.
/// All fields other than <see cref="Password"/> are required.
///
/// TenantUser callers may only supply <see cref="Password"/> and only for their
/// own user ID. TenantAdmin callers may update users within their tenancy but
/// cannot escalate them to a role higher than TenantUser.
/// </summary>
/// <param name="Username">New login username.</param>
/// <param name="Role">Role to assign. Allowed values: GlobalAdmin, TenantAdmin, TenantUser.</param>
/// <param name="TenancyId">Tenancy affiliation. Must be null for GlobalAdmin.</param>
/// <param name="Password">
/// Optional new password. When present the password is changed as part of the
/// same request. Must meet the configured ASP.NET Identity password policy.
/// </param>
public record UpdateUserRequest(
	string Username,
	string Role,
	Guid? TenancyId,
	string? Password
);

/// <summary>
/// Response shape returned when listing or creating users.
/// Passwords are never included in responses.
/// </summary>
/// <param name="Id">The ASP.NET Identity user ID (string GUID).</param>
/// <param name="Username">The user's login name.</param>
/// <param name="Role">The user's assigned role.</param>
/// <param name="TenancyId">The tenancy the user belongs to, or null for GlobalAdmin.</param>
public record UserResponse(
	string Id,
	string Username,
	string Role,
	Guid? TenancyId
);
