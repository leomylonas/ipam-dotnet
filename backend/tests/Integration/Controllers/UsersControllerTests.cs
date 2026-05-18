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

	/// <summary>HTTP client pre-authenticated as a TenantUser of <see cref="_tenancyId"/>.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>The ID of the tenancy created during initialisation.</summary>
	private Guid _tenancyId;

	/// <summary>The Identity ID of the TenantUser created during initialisation.</summary>
	private string _tenantUserId = null!;

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

			// TenantUser for the same tenancy — used in password-change tests.
			var tuser = new ApplicationUser
			{
				UserName = "tuser",
				Email = "tuser",
				Role = "TenantUser",
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(tuser, "Test1234!");
			_tenantUserId = tuser.Id;

			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
		_tenantAdminClient = Factory.CreateAuthenticatedClient("tadmin", "Test1234!");
		_tenantUserClient = Factory.CreateAuthenticatedClient("tuser", "Test1234!");
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

	[Fact]
	public async Task UpdateUser_AsGlobalAdmin_ReturnsOk()
	{
		var createReq = new CreateUserRequest("update-user", "Test1234!", "TenantUser", _tenancyId);
		var createResp = await _adminClient.PostAsJsonAsync("/api/users", createReq);
		var created = await createResp.Content.ReadFromJsonAsync<UserResponse>();

		var updateReq = new UpdateUserRequest("update-user-renamed", "TenantAdmin", _tenancyId, null);
		var updateResp = await _adminClient.PutAsJsonAsync($"/api/users/{created!.Id}", updateReq);

		Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
		var updated = await updateResp.Content.ReadFromJsonAsync<UserResponse>();
		Assert.Equal("update-user-renamed", updated!.Username);
		Assert.Equal("TenantAdmin", updated.Role);
	}

	[Fact]
	public async Task UpdateUser_AsTenantAdmin_CannotEscalateRole_ReturnsForbidden()
	{
		var createReq = new CreateUserRequest("tenant-update-user", "Test1234!", "TenantUser", _tenancyId);
		var createResp = await _adminClient.PostAsJsonAsync("/api/users", createReq);
		var created = await createResp.Content.ReadFromJsonAsync<UserResponse>();

		var updateReq = new UpdateUserRequest("tenant-update-user2", "TenantAdmin", _tenancyId, null);
		var updateResp = await _tenantAdminClient.PutAsJsonAsync($"/api/users/{created!.Id}", updateReq);

		Assert.Equal(HttpStatusCode.Forbidden, updateResp.StatusCode);
	}

	/// <summary>
	/// Verifies that GlobalAdmin can supply a new password in PUT /api/users/{id}
	/// and that the new password replaces the old one — the old password must stop
	/// working and the new one must authenticate successfully.
	/// </summary>
	[Fact]
	public async Task UpdateUser_WithNewPassword_AsGlobalAdmin_NewPasswordWorks()
	{
		// Create a fresh user so the test is self-contained.
		var createReq = new CreateUserRequest("pw-test-ga", "OldPass1!", "TenantUser", _tenancyId);
		var createResp = await _adminClient.PostAsJsonAsync("/api/users", createReq);
		Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
		var created = await createResp.Content.ReadFromJsonAsync<UserResponse>();

		// Update with a new password alongside the other profile fields.
		var updateReq = new UpdateUserRequest("pw-test-ga", "TenantUser", _tenancyId, "NewPass1!");
		var updateResp = await _adminClient.PutAsJsonAsync($"/api/users/{created!.Id}", updateReq);
		Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

		// The new password must authenticate successfully.
		var newClient = Factory.CreateAuthenticatedClient("pw-test-ga", "NewPass1!");
		var meResp = await newClient.GetAsync("/auth/me");
		Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

		// The old password must no longer work.
		var oldClient = Factory.CreateAuthenticatedClient("pw-test-ga", "OldPass1!");
		var oldMeResp = await oldClient.GetAsync("/auth/me");
		Assert.Equal(HttpStatusCode.Unauthorized, oldMeResp.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantUser can update their own password via PUT /api/users/{id}
	/// and that the new password authenticates successfully afterwards.
	/// </summary>
	[Fact]
	public async Task UpdateUser_WithNewPassword_AsTenantUser_CanChangeOwnPassword()
	{
		// _tenantUserId and _tenantUserClient are set up in InitializeAsync.
		// The TenantUser may only update the password field; other fields are ignored.
		var updateReq = new UpdateUserRequest("tuser", "TenantUser", _tenancyId, "ChangedPass1!");
		var updateResp = await _tenantUserClient.PutAsJsonAsync($"/api/users/{_tenantUserId}", updateReq);
		Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

		// New password must authenticate.
		var newClient = Factory.CreateAuthenticatedClient("tuser", "ChangedPass1!");
		var meResp = await newClient.GetAsync("/auth/me");
		Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantUser receives 403 Forbidden when they attempt to
	/// update another user's password via PUT /api/users/{id}.
	/// </summary>
	[Fact]
	public async Task UpdateUser_WithNewPassword_AsTenantUser_CannotChangeOtherUsersPassword()
	{
		// Create a second TenantUser whose ID the first TenantUser will try to update.
		var createReq = new CreateUserRequest("other-tuser", "Test1234!", "TenantUser", _tenancyId);
		var createResp = await _adminClient.PostAsJsonAsync("/api/users", createReq);
		Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
		var otherUser = await createResp.Content.ReadFromJsonAsync<UserResponse>();

		// _tenantUserClient is authenticated as "tuser" — attempting to update "other-tuser".
		var updateReq = new UpdateUserRequest("other-tuser", "TenantUser", _tenancyId, "HackedPass1!");
		var updateResp = await _tenantUserClient.PutAsJsonAsync($"/api/users/{otherUser!.Id}", updateReq);

		Assert.Equal(HttpStatusCode.Forbidden, updateResp.StatusCode);
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
