using System.Net;
using System.Net.Http.Json;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <c>TenanciesController</c>. Each test class instance
/// creates its own isolated SQLite database via <see cref="TestWebApplicationFactory"/>
/// and seeds a GlobalAdmin user before any HTTP requests are made.
///
/// Tests cover: happy paths, duplicate-name conflict, authentication requirement,
/// and delete (both success and not-found).
/// </summary>
public class TenanciesControllerTests : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	private readonly TestWebApplicationFactory _factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>
	/// Initialises a new instance of <see cref="TenanciesControllerTests"/> and
	/// creates the test web application factory.
	/// </summary>
	public TenanciesControllerTests()
	{
		_factory = new TestWebApplicationFactory();
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin user and creates the authenticated
	/// client. Called by xUnit before each test method in the class.
	/// </summary>
	public async Task InitializeAsync()
	{
		await _factory.SeedDatabaseAsync(async (db, um) =>
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
		_adminClient = _factory.CreateAuthenticatedClient("admin", "Test1234!");
	}

	/// <summary>
	/// Disposes the factory (and its database file) after all tests in the class
	/// have run. Called by xUnit after each test method.
	/// </summary>
	public Task DisposeAsync()
	{
		_factory.Dispose();
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
		var client = _factory.CreateClient();
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
}
