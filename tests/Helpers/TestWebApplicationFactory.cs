using IpamService.Data;
using IpamService.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IpamService.Tests.Helpers;

/// <summary>
/// A test-specific wrapper around <see cref="WebApplicationFactory{TProgram}"/> that
/// configures the application to use a per-instance isolated database, then cleans up
/// on disposal.
///
/// Design choices:
/// <list type="bullet">
///   <item><description>
///     <strong>Virtual <see cref="DatabaseProvider"/> and <see cref="DatabaseConnectionString"/></strong>
///     let derived classes (<c>MySqlTestWebApplicationFactory</c>,
///     <c>PostgresTestWebApplicationFactory</c>) plug in Testcontainer-backed databases
///     without duplicating any of the host configuration logic.
///   </description></item>
///   <item><description>
///     <strong>File-based SQLite</strong> (the default) rather than in-memory SQLite is
///     used because SQLite in-memory databases only persist for a single open connection.
///     EF Core opens and closes connections per-request, which drops the in-memory DB
///     between the startup migration and the first test request.
///   </description></item>
///   <item><description>
///     A unique temp-file path (or unique database name for server providers) per
///     factory instance means tests running in parallel cannot share or corrupt each
///     other's databases.
///   </description></item>
///   <item><description>
///     The seed admin username is overridden to a placeholder value so that
///     <c>Program.cs</c> startup does not create a real admin that would conflict
///     with test-controlled users created via <see cref="SeedDatabaseAsync"/>.
///   </description></item>
/// </list>
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
	/// <summary>
	/// Absolute path to the SQLite database file used when the provider is
	/// <c>sqlite</c>. Unique per instance to prevent cross-test contamination.
	/// Ignored by MySQL and PostgreSQL subclasses.
	/// </summary>
	private readonly string _dbPath;

	/// <summary>
	/// Initialises a new instance of <see cref="TestWebApplicationFactory"/> and
	/// reserves a unique SQLite file path in the system temp directory.
	/// </summary>
	public TestWebApplicationFactory()
	{
		// Generate a unique filename so parallel test classes do not share a DB.
		_dbPath = Path.Combine(Path.GetTempPath(), $"ipam_test_{Guid.NewGuid()}.db");
	}

	/// <summary>
	/// Gets the EF Core provider name to inject into the application configuration.
	/// Matches the values understood by <c>Program.cs</c>: <c>sqlite</c>, <c>mysql</c>,
	/// or <c>postgres</c>. Derived classes override this to redirect the app to a
	/// different database engine.
	/// </summary>
	protected virtual string DatabaseProvider => "sqlite";

	/// <summary>
	/// Gets the connection string to inject into the application configuration.
	/// Derived classes override this to point at a Testcontainer-managed server
	/// with a unique per-instance database name.
	/// </summary>
	protected virtual string DatabaseConnectionString => $"Data Source={_dbPath}";

	/// <summary>
	/// Overrides the web host configuration to redirect the application to the
	/// test database and suppress the startup seed so it does not interfere
	/// with test-controlled data.
	/// </summary>
	/// <param name="builder">The web host builder provided by the base class.</param>
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Inject test-specific configuration values that override appsettings.json.
		// AddInMemoryCollection is used instead of AddJsonFile so the test factory
		// does not need an appsettings file next to the test binary.
		builder.ConfigureAppConfiguration((_, cfg) =>
		{
			cfg.AddInMemoryCollection(new Dictionary<string, string?>
			{
				// Point the app at the per-instance database determined by the
				// virtual properties — allows subclasses to redirect to MySQL/Postgres.
				["Database:Provider"] = DatabaseProvider,
				["Database:ConnectionString"] = DatabaseConnectionString,

				// Use a placeholder admin name that no test will ever create so
				// Program.cs's seed block runs but creates a user that tests ignore.
				["Seed:AdminUsername"] = "_no_seed_admin_",
				["Seed:AdminPassword"] = "NoSeed1234!"
			});
		});

		builder.ConfigureServices(services =>
		{
			// Program.cs registers provider-specific subclasses via
			// AddDbContext<AppDbContext, TImplementation>, which keys options under
			// DbContextOptions<TImplementation> — not DbContextOptions<AppDbContext>.
			// Remove ALL provider DbContext and options registrations so none of the
			// production databases bleed into tests.
			var toRemove = services
				.Where(d =>
					d.ServiceType == typeof(AppDbContext) ||
					d.ServiceType == typeof(SqliteAppDbContext) ||
					d.ServiceType == typeof(MySqlAppDbContext) ||
					d.ServiceType == typeof(PostgresAppDbContext) ||
					d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
					d.ServiceType == typeof(DbContextOptions<SqliteAppDbContext>) ||
					d.ServiceType == typeof(DbContextOptions<MySqlAppDbContext>) ||
					d.ServiceType == typeof(DbContextOptions<PostgresAppDbContext>))
				.ToList();

			foreach (var descriptor in toRemove)
			{
				services.Remove(descriptor);
			}

			// Register the correct provider-specific DbContext subclass so that
			// Program.cs's db.Database.Migrate() call resolves a concrete type
			// whose migrations are attributed with [DbContext(typeof(T))].
			// Using the same AddDbContext<TContext, TImpl> pattern as production
			// ensures the DI graph is identical except for the connection string.
			switch (DatabaseProvider)
			{
				case "mysql":
					// Oracle MySql.EntityFrameworkCore — UseMySQL (capital SQL).
					services.AddDbContext<AppDbContext, MySqlAppDbContext>(options =>
						options.UseMySQL(DatabaseConnectionString,
							x => x.MigrationsAssembly("IpamService")));
					break;

				case "postgres":
					// Npgsql provider for PostgreSQL and compatible databases.
					services.AddDbContext<AppDbContext, PostgresAppDbContext>(options =>
						options.UseNpgsql(DatabaseConnectionString,
							x => x.MigrationsAssembly("IpamService")));
					break;

				default:
					// Default to SQLite for local development and CI — no server required.
					services.AddDbContext<AppDbContext, SqliteAppDbContext>(options =>
						options.UseSqlite(DatabaseConnectionString,
							x => x.MigrationsAssembly("IpamService")));
					break;
			}
		});
	}

	/// <summary>
	/// Cleans up the temporary SQLite database file when the factory is disposed.
	/// For MySQL and PostgreSQL providers the database lives inside a Testcontainer
	/// that is destroyed separately, so no additional cleanup is required here.
	/// </summary>
	/// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);

		// Only the SQLite provider creates a local file that needs manual cleanup.
		// MySQL and Postgres databases are destroyed with their Testcontainer.
		if (disposing && DatabaseProvider == "sqlite" && File.Exists(_dbPath))
		{
			File.Delete(_dbPath);
		}
	}

	/// <summary>
	/// Creates an <see cref="HttpClient"/> pre-configured with a
	/// <c>Authorization: Basic</c> header for the given credentials.
	/// </summary>
	/// <param name="username">The username to authenticate as.</param>
	/// <param name="password">The password to authenticate with.</param>
	/// <returns>An <see cref="HttpClient"/> that will send Basic auth on every request.</returns>
	public HttpClient CreateAuthenticatedClient(string username, string password)
	{
		var client = CreateClient();
		client.SetBasicAuth(username, password);
		return client;
	}

	/// <summary>
	/// Provides a scoped service scope in which the supplied delegate can
	/// seed the database with test data before any HTTP requests are made.
	/// The seeder receives the <see cref="AppDbContext"/> and
	/// <see cref="UserManager{TUser}"/> to allow both EF direct inserts
	/// and Identity-managed user creation.
	/// </summary>
	/// <param name="seeder">
	/// An async delegate that accepts an <see cref="AppDbContext"/> and a
	/// <see cref="UserManager{TUser}"/> and populates the database with test fixtures.
	/// </param>
	public async Task SeedDatabaseAsync(Func<AppDbContext, UserManager<ApplicationUser>, Task> seeder)
	{
		// Create a fresh DI scope so the seeder gets its own EF context and
		// UserManager, isolated from any request-scoped instances.
		using var scope = Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
		await seeder(db, userManager);
	}
}
