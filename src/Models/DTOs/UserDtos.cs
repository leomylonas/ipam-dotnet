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
/// Request body for PUT /api/users/{id}. Only non-password profile fields are
/// updated here; password changes are handled by dedicated endpoints.
/// </summary>
/// <param name="Username">New login username.</param>
/// <param name="Role">Role to assign. Allowed values: GlobalAdmin, TenantAdmin, TenantUser.</param>
/// <param name="TenancyId">Tenancy affiliation. Must be null for GlobalAdmin.</param>
public record UpdateUserRequest(
	string Username,
	string Role,
	Guid? TenancyId
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

/// <summary>
/// Request body for password-change endpoints (PUT /api/auth/password and
/// PUT /api/users/{id}/password). Old password is not required because
/// privilege checks on the endpoint itself enforce authorization.
/// </summary>
/// <param name="NewPassword">The new password. Must meet the configured password policy.</param>
public record ChangePasswordRequest(
	string NewPassword
);
