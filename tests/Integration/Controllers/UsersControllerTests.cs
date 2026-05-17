using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Abstract base class containing all integration tests for <c>UsersController</c>.
/// Concrete subclasses supply the factory so the same test logic runs against SQLite,
/// MySQL, and PostgreSQL without duplication.
///
/// Tests cover: listing users as GlobalAdmin vs TenantAdmin, creating users with
/// correct and incorrect role/tenancy combinations, and permission enforcement.
/// </summary>
public abstract class UsersControllerTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantAdmin of <see cref="_tenancyId"/>.</summary>
	private HttpClient _tenantAdminClient = null!;

	/// <summary>The ID of the tenancy created during initialisation.</summary>
	private Guid _tenancyId;

	/// <summary>
	/// Initialises a new instance of <see cref="UsersControllerTestsBase"/> using
	/// the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected UsersControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin, a tenancy, and a TenantAdmin for
	/// that tenancy. Creates authenticated clients for both roles.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			// GlobalAdmin — no tenancy affiliation.
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = "GlobalAdmin"
			};
			await um.CreateAsync(admin, "Test1234!");

			// Create a tenancy that the TenantAdmin will belong to.
			var tenancy = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TestTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			_tenancyId = tenancy.Id;
			db.Tenancies.Add(tenancy);

			// TenantAdmin for the new tenancy.
			var tadmin = new ApplicationUser
			{
				UserName = "tadmin",
				Email = "tadmin",
				Role = "TenantAdmin",
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(tadmin, "Test1234!");

			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
		_tenantAdminClient = Factory.CreateAuthenticatedClient("tadmin", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that GlobalAdmin receives all users in a 200 OK response.
	/// </summary>
	[Fact]
	public async Task ListUsers_AsGlobalAdmin_ReturnsAll()
	{
		var response = await _adminClient.GetAsync("/api/users");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var users = await response.Content.ReadFromJsonAsync<List<UserResponse>>();

		// At least the admin and tadmin seeded above should be present.
		Assert.NotEmpty(users!);
	}

	/// <summary>
	/// Verifies that TenantAdmin can list users but only sees users within their
	/// own tenancy (i.e. not the GlobalAdmin).
	/// </summary>
	[Fact]
	public async Task ListUsers_AsTenantAdmin_ReturnsOwnTenancy()
	{
		var response = await _tenantAdminClient.GetAsync("/api/users");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var users = await response.Content.ReadFromJsonAsync<List<UserResponse>>();

		// Every returned user must belong to the TenantAdmin's tenancy.
		Assert.All(users!, u => Assert.Equal(_tenancyId, u.TenancyId));
	}

	/// <summary>
	/// Verifies that GlobalAdmin can create a user with any role in any tenancy
	/// and receives a 201 Created response.
	/// </summary>
	[Fact]
	public async Task CreateUser_AsGlobalAdmin_ReturnsCreated()
	{
		var req = new CreateUserRequest("newuser", "Test1234!", "TenantUser", _tenancyId);
		var response = await _adminClient.PostAsJsonAsync("/api/users", req);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
	}

	/// <summary>
	/// Verifies that TenantAdmin cannot create a TenantAdmin user (role escalation
	/// is forbidden) and receives a 403 Forbidden response.
	/// </summary>
	[Fact]
	public async Task CreateUser_AsTenantAdmin_CanOnlyCreateTenantUser()
	{
		// TenantAdmin trying to create another TenantAdmin — should be rejected.
		var req = new CreateUserRequest("tuserabc", "Test1234!", "TenantAdmin", _tenancyId);
		var response = await _tenantAdminClient.PostAsJsonAsync("/api/users", req);
		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="UsersControllerTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class UsersControllerTests : UsersControllerTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public UsersControllerTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="UsersControllerTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class UsersControllerMySqlTests : UsersControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public UsersControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="UsersControllerTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class UsersControllerPostgresTests : UsersControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public UsersControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
