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
/// configures the application to use a dedicated SQLite file database for each test
/// class instance, then deletes the file on disposal.
///
/// Design choices:
/// <list type="bullet">
///   <item><description>
///     <strong>File-based SQLite</strong> rather than in-memory SQLite is used because
///     SQLite in-memory databases only persist for as long as a single connection is
///     open. EF Core opens and closes connections per-request, which drops the in-memory
///     DB between the startup migration and the first test request — causing
///     "no such table" errors.
///   </description></item>
///   <item><description>
///     A unique temp-file path per factory instance means tests running in parallel
///     cannot share or corrupt each other's databases.
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
	/// Absolute path to the SQLite database file used by this factory instance.
	/// Unique per instance to prevent cross-test contamination.
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
				// Point the app at the per-instance SQLite file.
				["Database:Provider"] = "sqlite",
				["Database:ConnectionString"] = $"Data Source={_dbPath}",

				// Use a placeholder admin name that no test will ever create so
				// Program.cs's seed block runs but creates a user that tests ignore.
				["Seed:AdminUsername"] = "_no_seed_admin_",
				["Seed:AdminPassword"] = "NoSeed1234!"
			});
		});

		builder.ConfigureServices(services =>
		{
			// Remove the DbContextOptions registered by Program.cs so we can
			// replace them with options pointing at the test database file.
			var descriptor = services.SingleOrDefault(d =>
				d.ServiceType == typeof(DbContextOptions<AppDbContext>));

			if (descriptor is not null)
			{
				services.Remove(descriptor);
			}

			// Register test-specific DbContext options targeting the temp SQLite file.
			services.AddDbContext<AppDbContext>(options =>
				options.UseSqlite($"Data Source={_dbPath}",
					x => x.MigrationsAssembly("IpamService")));
		});
	}

	/// <summary>
	/// Cleans up the temporary SQLite database file when the factory is disposed.
	/// This prevents temp-directory accumulation across test runs.
	/// </summary>
	/// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);

		if (disposing && File.Exists(_dbPath))
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
