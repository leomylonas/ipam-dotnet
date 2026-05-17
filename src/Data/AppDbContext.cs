using IpamService.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Data;

/// <summary>
/// Primary Entity Framework Core database context for the IPAM service.
/// Extends <see cref="IdentityDbContext{TUser}"/> to include ASP.NET Identity
/// tables alongside the IPAM-specific entity sets. All business data lives in
/// a single database; the provider (SQLite / MySQL / PostgreSQL) is selected
/// at startup based on configuration.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
	/// <summary>
	/// Initialises a new instance of <see cref="AppDbContext"/> with the
	/// supplied EF Core options. Options are injected by the DI container and
	/// include the connection string and provider chosen at startup.
	/// </summary>
	/// <param name="options">EF Core options, including provider and connection string.</param>
	public AppDbContext(DbContextOptions options) : base(options) { }

	/// <summary>All tenancies registered in the system.</summary>
	public DbSet<Tenancy> Tenancies => Set<Tenancy>();

	/// <summary>All subnets — both Shared and Private.</summary>
	public DbSet<Subnet> Subnets => Set<Subnet>();

	/// <summary>
	/// Join table that restricts shared subnets to specific tenancies.
	/// An absence of rows for a subnet means it is open to all tenancies.
	/// </summary>
	public DbSet<SubnetTenancyAccess> SubnetTenancyAccesses => Set<SubnetTenancyAccess>();

	/// <summary>IP range exclusion rules applied to subnets.</summary>
	public DbSet<Exclusion> Exclusions => Set<Exclusion>();

	/// <summary>All active IP allocations across all subnets and tenancies.</summary>
	public DbSet<Allocation> Allocations => Set<Allocation>();

	/// <summary>Freeform key-value tags attached to allocations.</summary>
	public DbSet<AllocationTag> AllocationTags => Set<AllocationTag>();

	/// <summary>Append-only audit trail of all significant system events.</summary>
	public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

	/// <summary>
	/// Configures entity mappings that cannot be expressed as data annotations.
	/// Sets up the composite primary key on <see cref="SubnetTenancyAccess"/>,
	/// the unique index on <see cref="Tenancy.Name"/>, and the composite unique
	/// index enforcing one value per tag key per allocation.
	/// </summary>
	/// <param name="builder">The model builder provided by EF Core.</param>
	protected override void OnModelCreating(ModelBuilder builder)
	{
		// Let Identity configure its own tables first (AspNetUsers, AspNetRoles, etc.)
		base.OnModelCreating(builder);

		// SubnetTenancyAccess has no surrogate key — the pair (SubnetId, TenancyId)
		// is the natural primary key, which EF Core cannot infer by convention.
		builder.Entity<SubnetTenancyAccess>()
			.HasKey(x => new { x.SubnetId, x.TenancyId });

		// Tenancy names must be unique across the entire system to avoid ambiguity
		// when referencing tenancies by name in tooling or reports.
		builder.Entity<Tenancy>()
			.HasIndex(x => x.Name)
			.IsUnique();

		// Enforce the business rule: each tag key may appear at most once per
		// allocation. This prevents duplicate-key confusion and makes PUT (full
		// replace) predictable.
		builder.Entity<AllocationTag>()
			.HasIndex(x => new { x.AllocationId, x.Key })
			.IsUnique();
	}
}
