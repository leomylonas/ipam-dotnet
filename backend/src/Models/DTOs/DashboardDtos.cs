namespace IpamService.Models.DTOs;

// ── Shared ────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated IP utilisation statistics for one or more subnets.
/// Used in both the global and tenant dashboard responses.
/// </summary>
/// <param name="TotalIps">Total usable IP addresses (network and broadcast excluded).</param>
/// <param name="AllocatedIps">Number of IPs currently allocated.</param>
/// <param name="FreeIps">Number of IPs that are neither allocated nor excluded.</param>
/// <param name="ExcludedIps">Number of IPs covered by exclusion ranges.</param>
/// <param name="UtilisationPercent">Ratio of allocated IPs to total IPs, expressed as a percentage.</param>
public record SubnetUtilisationDto(
	int TotalIps,
	int AllocatedIps,
	int FreeIps,
	int ExcludedIps,
	double UtilisationPercent
);

// ── GlobalAdmin dashboard ─────────────────────────────────────────────────────

/// <summary>
/// A subnet that has exceeded the configured exhaustion threshold, as seen
/// in the GlobalAdmin dashboard. Includes tenancy context for cross-tenancy visibility.
/// </summary>
/// <param name="SubnetId">ID of the subnet approaching exhaustion.</param>
/// <param name="Cidr">CIDR notation of the subnet.</param>
/// <param name="TenancyId">Owning tenancy ID, or null for shared subnets.</param>
/// <param name="TenancyName">Owning tenancy name, or null for shared subnets.</param>
/// <param name="UtilisationPercent">Current utilisation percentage.</param>
public record GlobalExhaustionAlert(
	Guid SubnetId,
	string Cidr,
	Guid? TenancyId,
	string? TenancyName,
	double UtilisationPercent
);

/// <summary>
/// A single audit log entry as shown on the GlobalAdmin dashboard.
/// Includes tenancy context since the global view spans all tenancies.
/// </summary>
/// <param name="Id">The audit entry's unique identifier.</param>
/// <param name="Timestamp">UTC timestamp of the action.</param>
/// <param name="Action">Short action verb, e.g. Allocated, SubnetCreated.</param>
/// <param name="UserId">Raw user ID of the performing user — enables the UI to copy it.</param>
/// <param name="PerformedBy">Username of the user who performed the action.</param>
/// <param name="TenancyId">Raw tenancy ID, or null for GlobalAdmin actions.</param>
/// <param name="TenancyName">Name of the tenancy context, or null for GlobalAdmin actions.</param>
/// <param name="Detail">Optional extra context from the audit entry's Notes field.</param>
public record GlobalDashboardAuditEntry(
	Guid Id,
	DateTime Timestamp,
	string Action,
	string UserId,
	string PerformedBy,
	Guid? TenancyId,
	string? TenancyName,
	string? Detail
);

/// <summary>
/// Response body for GET /dashboard/global. Contains system-wide statistics,
/// exhaustion alerts across all subnets, and the 10 most recent audit entries.
/// </summary>
/// <param name="TenancyCount">Total number of tenancies in the system.</param>
/// <param name="UserCount">Total number of user accounts in the system.</param>
/// <param name="SharedSubnetCount">Number of shared subnets managed by GlobalAdmin.</param>
/// <param name="SharedSubnetUtilisation">Aggregated utilisation across all shared subnets.</param>
/// <param name="SubnetsApproachingExhaustion">All subnets exceeding the configured threshold.</param>
/// <param name="RecentAuditEntries">The 10 most recent audit log entries across all tenancies.</param>
public record GlobalDashboardResponse(
	int TenancyCount,
	int UserCount,
	int SharedSubnetCount,
	SubnetUtilisationDto SharedSubnetUtilisation,
	List<GlobalExhaustionAlert> SubnetsApproachingExhaustion,
	List<GlobalDashboardAuditEntry> RecentAuditEntries
);

// ── TenantAdmin dashboard ─────────────────────────────────────────────────────

/// <summary>
/// A subnet approaching exhaustion as seen in the TenantAdmin dashboard.
/// Does not include tenancy context since the view is already scoped to one tenancy.
/// </summary>
/// <param name="SubnetId">ID of the subnet approaching exhaustion.</param>
/// <param name="Cidr">CIDR notation of the subnet.</param>
/// <param name="UtilisationPercent">Current utilisation percentage.</param>
public record TenantExhaustionAlert(
	Guid SubnetId,
	string Cidr,
	double UtilisationPercent
);

/// <summary>
/// A single audit log entry as shown on the TenantAdmin dashboard.
/// Tenancy context is omitted since the view is already scoped to one tenancy.
/// </summary>
/// <param name="Id">The audit entry's unique identifier.</param>
/// <param name="Timestamp">UTC timestamp of the action.</param>
/// <param name="Action">Short action verb.</param>
/// <param name="UserId">Raw user ID of the performing user — enables the UI to copy it.</param>
/// <param name="PerformedBy">Username of the user who performed the action.</param>
/// <param name="Detail">Optional extra context from the audit entry's Notes field.</param>
public record TenantDashboardAuditEntry(
	Guid Id,
	DateTime Timestamp,
	string Action,
	string UserId,
	string PerformedBy,
	string? Detail
);

/// <summary>
/// Response body for GET /dashboard/tenant. Contains statistics scoped to the
/// caller's tenancy, exhaustion alerts for accessible subnets, and the 10 most
/// recent audit entries within the tenancy.
/// </summary>
/// <param name="TenancyId">The caller's tenancy ID.</param>
/// <param name="TenancyName">The caller's tenancy name.</param>
/// <param name="UserCount">Number of users within this tenancy.</param>
/// <param name="PrivateSubnetCount">Number of private subnets owned by this tenancy.</param>
/// <param name="PrivateSubnetUtilisation">Aggregated utilisation across the tenancy's private subnets.</param>
/// <param name="AccessibleSharedSubnetCount">Number of shared subnets accessible to this tenancy.</param>
/// <param name="SubnetsApproachingExhaustion">Subnets accessible to this tenancy that exceed the threshold.</param>
/// <param name="RecentAuditEntries">The 10 most recent audit log entries for this tenancy.</param>
public record TenantDashboardResponse(
	Guid TenancyId,
	string TenancyName,
	int UserCount,
	int PrivateSubnetCount,
	SubnetUtilisationDto PrivateSubnetUtilisation,
	int AccessibleSharedSubnetCount,
	List<TenantExhaustionAlert> SubnetsApproachingExhaustion,
	List<TenantDashboardAuditEntry> RecentAuditEntries
);

// ── TenantUser dashboard ──────────────────────────────────────────────────────

/// <summary>
/// A summary of a recent allocation visible on the TenantUser dashboard.
/// </summary>
/// <param name="Id">The allocation's unique identifier.</param>
/// <param name="IpAddress">The allocated IP address.</param>
/// <param name="SubnetCidr">CIDR of the subnet the IP was allocated from.</param>
/// <param name="AllocatedAt">UTC timestamp when the allocation was made.</param>
/// <param name="Tags">Freeform key-value tags on the allocation.</param>
public record RecentAllocationDto(
	Guid Id,
	string IpAddress,
	string SubnetCidr,
	DateTime AllocatedAt,
	Dictionary<string, string> Tags
);

/// <summary>
/// A summary of a subnet accessible to the TenantUser, showing available capacity.
/// </summary>
/// <param name="SubnetId">The subnet's unique identifier.</param>
/// <param name="Cidr">CIDR notation of the subnet.</param>
/// <param name="FreeIps">Number of IPs currently available for allocation.</param>
public record AccessibleSubnetDto(
	Guid SubnetId,
	string Cidr,
	int FreeIps
);

/// <summary>
/// Response body for GET /dashboard/user. Shows the TenantUser their recent
/// allocations and the subnets they have access to.
/// </summary>
/// <param name="RecentAccessibleAllocations">Recent allocations made by this tenancy, newest first.</param>
/// <param name="AccessibleSubnets">Subnets the caller's tenancy can allocate from, with free IP counts.</param>
public record UserDashboardResponse(
	List<RecentAllocationDto> RecentAccessibleAllocations,
	List<AccessibleSubnetDto> AccessibleSubnets
);

// ── Auth ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /auth/login. Credentials are sent as a JSON body so
/// that the React UI can submit a standard login form without constructing a
/// Basic Auth header manually.
/// </summary>
/// <param name="Username">The account username.</param>
/// <param name="Password">The account password.</param>
public record LoginRequest(string Username, string Password);

/// <summary>
/// Response body returned by POST /auth/login and GET /auth/me. Carries
/// enough information for the React router to determine which pages the
/// caller is allowed to visit without a separate profile fetch.
/// </summary>
/// <param name="Id">The ASP.NET Identity user ID.</param>
/// <param name="Username">The user's login name.</param>
/// <param name="Role">The user's role: GlobalAdmin, TenantAdmin, or TenantUser.</param>
/// <param name="TenancyId">The tenancy the user belongs to, or null for GlobalAdmin.</param>
public record AuthMeResponse(
	string Id,
	string Username,
	string Role,
	Guid? TenancyId
);
