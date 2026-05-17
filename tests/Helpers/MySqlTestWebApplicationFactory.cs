namespace IpamService.Tests.Helpers;

/// <summary>
/// A <see cref="TestWebApplicationFactory"/> variant that redirects the application
/// to a MySQL database running inside a Testcontainer.
///
/// Each instance generates a unique database name (GUID-based) within the shared
/// <see cref="MySqlContainerFixture"/> server, so test classes that run in parallel
/// within the <c>mysql</c> xUnit collection cannot corrupt each other's data.
///
/// The database is created automatically by EF Core's <c>Migrate()</c> call during
/// application startup; no pre-creation step is required.
/// Because the entire MySQL container is destroyed at the end of the test run, there
/// is no need to drop the per-class database on disposal.
/// </summary>
public sealed class MySqlTestWebApplicationFactory : TestWebApplicationFactory
{
	/// <summary>
	/// The shared container fixture that provides the running MySQL server.
	/// Injected via the constructor by xUnit's collection fixture infrastructure.
	/// </summary>
	private readonly MySqlContainerFixture _fixture;

	/// <summary>
	/// Unique database name for this factory instance. Using a GUID ensures that
	/// even when multiple test classes start simultaneously (xUnit parallelism
	/// within the collection is disabled, but the name must still be stable) there
	/// is no collision.
	/// </summary>
	private readonly string _dbName;

	/// <summary>
	/// Initialises a new instance of <see cref="MySqlTestWebApplicationFactory"/>
	/// with a unique database name derived from a new GUID.
	/// </summary>
	/// <param name="fixture">
	/// The shared <see cref="MySqlContainerFixture"/> that owns the running container.
	/// </param>
	public MySqlTestWebApplicationFactory(MySqlContainerFixture fixture)
	{
		_fixture = fixture;

		// Use only hex digits — MySQL database names must be valid identifiers.
		// The "ipam_" prefix makes it easy to spot these databases in logs.
		_dbName = $"ipam_{Guid.NewGuid():N}";
	}

	/// <summary>
	/// Overrides the base provider to <c>mysql</c>, causing
	/// <see cref="TestWebApplicationFactory.ConfigureWebHost"/> to register
	/// <c>MySqlAppDbContext</c> instead of <c>SqliteAppDbContext</c>.
	/// </summary>
	protected override string DatabaseProvider => "mysql";

	/// <summary>
	/// Returns a connection string for this instance's unique database within the
	/// shared Testcontainer MySQL server.
	/// </summary>
	protected override string DatabaseConnectionString => _fixture.GetConnectionString(_dbName);
}
