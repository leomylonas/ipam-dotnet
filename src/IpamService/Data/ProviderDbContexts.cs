using Microsoft.EntityFrameworkCore;

namespace IpamService.Data;

/// <summary>
/// Provider-specific DbContext used exclusively for SQLite migrations.
/// Runtime services still depend on <see cref="AppDbContext"/>; DI binds that
/// service to one of these concrete implementations based on configuration.
/// </summary>
public sealed class SqliteAppDbContext : AppDbContext
{
	public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : base(options) { }
}

/// <summary>
/// Provider-specific DbContext used exclusively for MySQL migrations.
/// </summary>
public sealed class MySqlAppDbContext : AppDbContext
{
	public MySqlAppDbContext(DbContextOptions<MySqlAppDbContext> options) : base(options) { }
}

/// <summary>
/// Provider-specific DbContext used exclusively for PostgreSQL migrations.
/// </summary>
public sealed class PostgresAppDbContext : AppDbContext
{
	public PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : base(options) { }
}
