using Testcontainers.MySql;

namespace IpamService.Tests.Helpers;

/// <summary>
/// Declares the xUnit collection that shares a single <see cref="MySqlContainerFixture"/>
/// across every test class decorated with <c>[Collection("mysql")]</c>. Sharing the
/// container at the collection level means MySQL is started once per test run rather
/// than once per test class, which keeps the suite fast while still allowing each
/// test class to use its own isolated database within the same server.
/// </summary>
[CollectionDefinition("mysql")]
public class MySqlCollection : ICollectionFixture<MySqlContainerFixture> { }

/// <summary>
/// xUnit collection fixture that owns a single MySQL Testcontainer for the lifetime
/// of the test run. All test classes in the <c>mysql</c> collection share this
/// fixture and each receive a unique database name via
/// <see cref="GetConnectionString"/>.
///
/// The container is started in <see cref="InitializeAsync"/> (called once before any
/// test class in the collection runs) and stopped in <see cref="DisposeAsync"/>
/// (called once after all test classes have finished).
/// </summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
	/// <summary>
	/// The Testcontainer instance. A fresh container is created per fixture lifetime
	/// (i.e. per test run), so there are no cross-run side effects.
	/// </summary>
	private readonly MySqlContainer _container;

	/// <summary>
	/// Initialises a new instance of <see cref="MySqlContainerFixture"/> and
	/// configures the MySQL container. The container is not started here; that
	/// happens in <see cref="InitializeAsync"/>.
	/// </summary>
	public MySqlContainerFixture()
	{
		// Build the container with a known root password so we can connect with
		// sufficient privileges to CREATE DATABASE for each test class.
		// The MySqlBuilder module has a built-in wait strategy that waits until
		// MySQL is accepting connections, so no custom WithWaitStrategy is needed.
#pragma warning disable CS0618 // parameterless constructor deprecated in 4.x; image defaulting is intentional here
		_container = new MySqlBuilder()
#pragma warning restore CS0618
			.WithUsername("root")
			.WithPassword("Test1234!")
			.WithDatabase("ipam_root")
			.Build();
	}

	/// <summary>
	/// Returns a provider-appropriate connection string pointing at the given
	/// database name within the running MySQL container.
	/// Each <see cref="MySqlTestWebApplicationFactory"/> calls this with its own
	/// GUID-derived database name, ensuring complete isolation between test classes.
	/// </summary>
	/// <param name="database">
	/// The target database name. EF Core's <c>Migrate()</c> will create it if it
	/// does not yet exist.
	/// </param>
	/// <returns>
	/// A connection string compatible with Oracle's <c>MySql.EntityFrameworkCore</c>
	/// provider (uses <c>User Id</c> syntax required by <c>MySql.Data</c>).
	/// </returns>
	public string GetConnectionString(string database)
	{
		// Build the connection string from container properties rather than parsing
		// the container's built-in GetConnectionString(), which uses MySqlConnector
		// syntax that is incompatible with Oracle's MySql.Data connector.
		return $"Server={_container.Hostname};"
			+ $"Port={_container.GetMappedPublicPort(3306)};"
			+ $"Database={database};"
			+ $"User Id=root;"
			+ $"Password=Test1234!;"
			+ "AllowUserVariables=True;";
	}

	/// <summary>
	/// Starts the MySQL container. Called once by xUnit before any test class in
	/// the <c>mysql</c> collection begins executing.
	/// </summary>
	public async Task InitializeAsync()
	{
		await _container.StartAsync();
	}

	/// <summary>
	/// Stops and removes the MySQL container. Called once by xUnit after all test
	/// classes in the <c>mysql</c> collection have finished executing.
	/// </summary>
	public async Task DisposeAsync()
	{
		await _container.DisposeAsync();
	}
}
