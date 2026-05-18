using System.Net;
using System.Net.Http.Json;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Abstract base class containing all integration tests for <c>TenanciesController</c>.
/// Concrete subclasses supply the factory so the same test logic runs against SQLite,
/// MySQL, and PostgreSQL without duplication.
///
/// Tests cover: happy paths, duplicate-name conflict, authentication requirement,
/// and delete (both success and not-found).
/// </summary>
public abstract class TenanciesControllerTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>
	/// Initialises a new instance of <see cref="TenanciesControllerTestsBase"/>
	/// using the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">
	/// The <see cref="TestWebApplicationFactory"/> (or subclass) that controls which
	/// database engine is used for this test run.
	/// </param>
	protected TenanciesControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin user and creates the authenticated
	/// client. Called by xUnit before each test method in the class.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			// Create the GlobalAdmin user that the authenticated client will use.
			var admin = new IpamService.Models.ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = "GlobalAdmin"
			};
			await um.CreateAsync(admin, "Test1234!");
		});

		// Build an HTTP client that sends Basic auth on every request.
		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
	}

	/// <summary>
	/// Disposes the factory (and its database) after all tests in the class have run.
	/// </summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that an authenticated GlobalAdmin can list tenancies and receives
	/// a 200 OK response.
	/// </summary>
	[Fact]
	public async Task GetTenancies_ReturnsOk()
	{
		var response = await _adminClient.GetAsync("/api/tenancies");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	/// <summary>
	/// Verifies that creating a tenancy with valid data returns 201 Created.
	/// </summary>
	[Fact]
	public async Task CreateTenancy_ReturnsCreated()
	{
		var req = new CreateTenancyRequest("TestTenancy", "Desc", "tadmin", "Test1234!");
		var response = await _adminClient.PostAsJsonAsync("/api/tenancies", req);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
	}

	/// <summary>
	/// Verifies that attempting to create a second tenancy with the same name returns
	/// 409 Conflict.
	/// </summary>
	[Fact]
	public async Task CreateTenancy_DuplicateName_ReturnsConflict()
	{
		// Create the first tenancy with name "DupTenancy".
		var req = new CreateTenancyRequest("DupTenancy", "Desc", "user1", "Test1234!");
		await _adminClient.PostAsJsonAsync("/api/tenancies", req);

		// A second request with the same name should be rejected.
		var req2 = new CreateTenancyRequest("DupTenancy", "Desc", "user2", "Test1234!");
		var response = await _adminClient.PostAsJsonAsync("/api/tenancies", req2);
		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	/// <summary>
	/// Verifies that an unauthenticated request receives 401 Unauthorized.
	/// </summary>
	[Fact]
	public async Task GetTenancies_Unauthenticated_Returns401()
	{
		// Use a raw (unauthenticated) client — no auth header set.
		var client = Factory.CreateClient();
		var response = await client.GetAsync("/api/tenancies");
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	/// <summary>
	/// Verifies that deleting an existing tenancy returns 204 No Content.
	/// </summary>
	[Fact]
	public async Task DeleteTenancy_ReturnsNoContent()
	{
		// Create a tenancy to delete.
		var req = new CreateTenancyRequest("ToDelete", "Desc", "deluser", "Test1234!");
		var createResponse = await _adminClient.PostAsJsonAsync("/api/tenancies", req);
		var created = await createResponse.Content.ReadFromJsonAsync<TenancyResponse>();

		// Delete the tenancy by its ID.
		var deleteResponse = await _adminClient.DeleteAsync($"/api/tenancies/{created!.Id}");
		Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
	}

	/// <summary>
	/// Verifies that attempting to delete a tenancy with a non-existent ID returns
	/// 404 Not Found.
	/// </summary>
	[Fact]
	public async Task DeleteTenancy_NotFound_Returns404()
	{
		// Use a random GUID that does not correspond to any tenancy in the database.
		var response = await _adminClient.DeleteAsync($"/api/tenancies/{Guid.NewGuid()}");
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task UpdateTenancy_ReturnsOk()
	{
		var createReq = new CreateTenancyRequest("UpdateMe", "Before", "update-admin", "Test1234!");
		var createResponse = await _adminClient.PostAsJsonAsync("/api/tenancies", createReq);
		var tenancy = await createResponse.Content.ReadFromJsonAsync<TenancyResponse>();

		var updateReq = new UpdateTenancyRequest("UpdateMeRenamed", "After");
		var updateResponse = await _adminClient.PutAsJsonAsync($"/api/tenancies/{tenancy!.Id}", updateReq);

		Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
		var updated = await updateResponse.Content.ReadFromJsonAsync<TenancyResponse>();
		Assert.Equal("UpdateMeRenamed", updated!.Name);
		Assert.Equal("After", updated.Description);
	}

	[Fact]
	public async Task UpdateTenancy_DuplicateName_ReturnsConflict()
	{
		await _adminClient.PostAsJsonAsync("/api/tenancies",
			new CreateTenancyRequest("TenancyA", "A", "ta-admin", "Test1234!"));
		var secondCreate = await _adminClient.PostAsJsonAsync("/api/tenancies",
			new CreateTenancyRequest("TenancyB", "B", "tb-admin", "Test1234!"));
		var second = await secondCreate.Content.ReadFromJsonAsync<TenancyResponse>();

		var response = await _adminClient.PutAsJsonAsync(
			$"/api/tenancies/{second!.Id}",
			new UpdateTenancyRequest("TenancyA", "Renamed"));

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="TenanciesControllerTestsBase"/> against an isolated SQLite
/// file database — the default for local development and CI.
/// </summary>
public class TenanciesControllerTests : TenanciesControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a fresh per-instance SQLite file database.
	/// </summary>
	public TenanciesControllerTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="TenanciesControllerTestsBase"/> against a MySQL database
/// provisioned by a shared Testcontainer. Each instance gets its own isolated
/// database within the container.
/// </summary>
[Collection("mysql")]
public class TenanciesControllerMySqlTests : TenanciesControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory connected to the shared
	/// Testcontainer server.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public TenanciesControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="TenanciesControllerTestsBase"/> against a PostgreSQL database
/// provisioned by a shared Testcontainer. Each instance gets its own isolated
/// database within the container.
/// </summary>
[Collection("postgres")]
public class TenanciesControllerPostgresTests : TenanciesControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory connected to the shared
	/// Testcontainer server.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public TenanciesControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
