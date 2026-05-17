using Testcontainers.PostgreSql;

namespace IpamService.Tests.Helpers;

/// <summary>
/// Declares the xUnit collection that shares a single <see cref="PostgresContainerFixture"/>
/// across every test class decorated with <c>[Collection("postgres")]</c>. Sharing the
/// container at the collection level means PostgreSQL is started once per test run
/// rather than once per test class.
/// </summary>
[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresContainerFixture> { }

/// <summary>
/// xUnit collection fixture that owns a single PostgreSQL Testcontainer for the
/// lifetime of the test run. All test classes in the <c>postgres</c> collection share
/// this fixture and each receive a unique database name via
/// <see cref="GetConnectionString"/>.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	/// <summary>
	/// The Testcontainer instance. Started once per test run, then disposed.
	/// </summary>
	private readonly PostgreSqlContainer _container;

	/// <summary>
	/// Initialises a new instance of <see cref="PostgresContainerFixture"/> and
	/// configures the PostgreSQL container. The container is not started here.
	/// </summary>
	public PostgresContainerFixture()
	{
		// Build the container with known credentials so we can connect as a
		// superuser and let EF Core's Migrate() create per-class databases.
		// The PostgreSqlBuilder module has a built-in wait strategy that waits
		// until PostgreSQL is accepting connections, so no custom wait is needed.
#pragma warning disable CS0618 // parameterless constructor deprecated in 4.x; image defaulting is intentional here
		_container = new PostgreSqlBuilder()
#pragma warning restore CS0618
			.WithUsername("postgres")
			.WithPassword("Test1234!")
			.WithDatabase("ipam_root")
			.Build();
	}

	/// <summary>
	/// Returns a Npgsql-compatible connection string pointing at the given
	/// database name within the running PostgreSQL container.
	/// Each <see cref="PostgresTestWebApplicationFactory"/> calls this with its own
	/// GUID-derived database name, ensuring complete isolation between test classes.
	/// </summary>
	/// <param name="database">
	/// The target database name. EF Core's <c>Migrate()</c> will create it if it
	/// does not yet exist.
	/// </param>
	/// <returns>
	/// A Npgsql connection string compatible with <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>.
	/// </returns>
	public string GetConnectionString(string database)
	{
		return $"Host={_container.Hostname};"
			+ $"Port={_container.GetMappedPublicPort(5432)};"
			+ $"Database={database};"
			+ $"Username=postgres;"
			+ $"Password=Test1234!;";
	}

	/// <summary>
	/// Starts the PostgreSQL container. Called once by xUnit before any test class
	/// in the <c>postgres</c> collection begins executing.
	/// </summary>
	public async Task InitializeAsync()
	{
		await _container.StartAsync();
	}

	/// <summary>
	/// Stops and removes the PostgreSQL container. Called once by xUnit after all
	/// test classes in the <c>postgres</c> collection have finished executing.
	/// </summary>
	public async Task DisposeAsync()
	{
		await _container.DisposeAsync();
	}
}
