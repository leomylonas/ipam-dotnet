namespace IpamService.Tests.Helpers;

/// <summary>
/// A <see cref="TestWebApplicationFactory"/> variant that redirects the application
/// to a PostgreSQL database running inside a Testcontainer.
///
/// Each instance generates a unique database name (GUID-based) within the shared
/// <see cref="PostgresContainerFixture"/> server, so test classes cannot corrupt
/// each other's data.
///
/// The database is created automatically by EF Core's <c>Migrate()</c> call during
/// application startup. The container is destroyed at the end of the test run, so
/// no per-class database cleanup is required.
/// </summary>
public sealed class PostgresTestWebApplicationFactory : TestWebApplicationFactory
{
	/// <summary>
	/// The shared container fixture that provides the running PostgreSQL server.
	/// </summary>
	private readonly PostgresContainerFixture _fixture;

	/// <summary>
	/// Unique database name for this factory instance.
	/// PostgreSQL database names are lowercase and limited to 63 bytes; the
	/// hex-encoded GUID fits comfortably within that limit.
	/// </summary>
	private readonly string _dbName;

	/// <summary>
	/// Initialises a new instance of <see cref="PostgresTestWebApplicationFactory"/>
	/// with a unique database name derived from a new GUID.
	/// </summary>
	/// <param name="fixture">
	/// The shared <see cref="PostgresContainerFixture"/> that owns the running container.
	/// </param>
	public PostgresTestWebApplicationFactory(PostgresContainerFixture fixture)
	{
		_fixture = fixture;

		// PostgreSQL identifiers are case-folded to lowercase; the "ipam_" prefix
		// keeps the name recognisable in pg_catalog queries and server logs.
		_dbName = $"ipam_{Guid.NewGuid():N}";
	}

	/// <summary>
	/// Overrides the base provider to <c>postgres</c>, causing
	/// <see cref="TestWebApplicationFactory.ConfigureWebHost"/> to register
	/// <c>PostgresAppDbContext</c> instead of <c>SqliteAppDbContext</c>.
	/// </summary>
	protected override string DatabaseProvider => "postgres";

	/// <summary>
	/// Returns a Npgsql connection string for this instance's unique database within
	/// the shared Testcontainer PostgreSQL server.
	/// </summary>
	protected override string DatabaseConnectionString => _fixture.GetConnectionString(_dbName);
}
