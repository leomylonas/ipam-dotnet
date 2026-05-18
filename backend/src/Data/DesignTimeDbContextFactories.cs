using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IpamService.Data;

public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
	public SqliteAppDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<SqliteAppDbContext>();
		optionsBuilder.UseSqlite(
			"Data Source=ipam-design-sqlite.db",
			x => x.MigrationsAssembly("IpamService"));

		return new SqliteAppDbContext(optionsBuilder.Options);
	}
}

public sealed class MySqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MySqlAppDbContext>
{
	public MySqlAppDbContext CreateDbContext(string[] args)
	{
		const string connStr = "Server=localhost;Port=3306;Database=ipam_design;User=root;Password=pass;";

		var optionsBuilder = new DbContextOptionsBuilder<MySqlAppDbContext>();
		optionsBuilder.UseMySQL(
			connStr,
			x => x.MigrationsAssembly("IpamService"));

		return new MySqlAppDbContext(optionsBuilder.Options);
	}
}

public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
	public PostgresAppDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<PostgresAppDbContext>();
		optionsBuilder.UseNpgsql(
			"Host=localhost;Port=5432;Database=ipam_design;Username=postgres;Password=pass",
			x => x.MigrationsAssembly("IpamService"));

		return new PostgresAppDbContext(optionsBuilder.Options);
	}
}
