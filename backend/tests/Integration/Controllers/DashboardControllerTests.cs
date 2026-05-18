using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Abstract base class containing all integration tests for <c>DashboardController</c>.
/// Concrete subclasses supply the factory so the same test logic runs against SQLite,
/// MySQL, and PostgreSQL without duplication.
///
/// Tests cover: role-based access control (right role → 200, wrong role → 403) and
/// response-shape validation for each of the three dashboard endpoints.
/// </summary>
public abstract class DashboardControllerTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantAdmin.</summary>
	private HttpClient _tenantAdminClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantUser.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>The ID of the tenancy created during initialisation.</summary>
	private Guid _tenancyId;

	/// <summary>
	/// Initialises a new instance of <see cref="DashboardControllerTestsBase"/> using
	/// the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected DashboardControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with one user for each role — a GlobalAdmin, a TenantAdmin,
	/// and a TenantUser — plus the tenancy the tenant-scoped users belong to.
	/// Creates an authenticated HTTP client for each role.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			// GlobalAdmin — system-wide, no tenancy.
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = Roles.GlobalAdmin
			};
			await um.CreateAsync(admin, "Test1234!");

			// Tenancy for the tenant-scoped users.
			var tenancy = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "DashTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			_tenancyId = tenancy.Id;
			db.Tenancies.Add(tenancy);

			// TenantAdmin for the tenancy above.
			var tadmin = new ApplicationUser
			{
				UserName = "tadmin",
				Email = "tadmin",
				Role = Roles.TenantAdmin,
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(tadmin, "Test1234!");

			// TenantUser for the same tenancy.
			var tuser = new ApplicationUser
			{
				UserName = "tuser",
				Email = "tuser",
				Role = Roles.TenantUser,
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(tuser, "Test1234!");

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

	// ── GET /dashboard/global ─────────────────────────────────────────────────

	/// <summary>
	/// Verifies that a GlobalAdmin receives 200 OK from GET /dashboard/global with
	/// a correctly shaped <see cref="GlobalDashboardResponse"/> body.
	/// </summary>
	[Fact]
	public async Task GlobalDashboard_AsGlobalAdmin_Returns200WithCorrectShape()
	{
		var response = await _adminClient.GetAsync("/dashboard/global");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<GlobalDashboardResponse>();
		Assert.NotNull(body);
		// Counts must be non-negative; with the seeded data there is at least one
		// tenancy and three users (admin, tadmin, tuser).
		Assert.True(body.TenancyCount >= 1);
		Assert.True(body.UserCount >= 3);
		Assert.NotNull(body.SharedSubnetUtilisation);
		Assert.NotNull(body.SubnetsApproachingExhaustion);
		Assert.NotNull(body.RecentAuditEntries);
	}

	/// <summary>
	/// Verifies that a TenantAdmin receives 403 Forbidden from GET /dashboard/global.
	/// </summary>
	[Fact]
	public async Task GlobalDashboard_AsTenantAdmin_Returns403()
	{
		var response = await _tenantAdminClient.GetAsync("/dashboard/global");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantUser receives 403 Forbidden from GET /dashboard/global.
	/// </summary>
	[Fact]
	public async Task GlobalDashboard_AsTenantUser_Returns403()
	{
		var response = await _tenantUserClient.GetAsync("/dashboard/global");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that an unauthenticated request to GET /dashboard/global returns 401.
	/// </summary>
	[Fact]
	public async Task GlobalDashboard_Unauthenticated_Returns401()
	{
		var client = Factory.CreateClient();

		var response = await client.GetAsync("/dashboard/global");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	// ── GET /dashboard/tenant ─────────────────────────────────────────────────

	/// <summary>
	/// Verifies that a TenantAdmin receives 200 OK from GET /dashboard/tenant with
	/// a correctly shaped <see cref="TenantDashboardResponse"/> scoped to their tenancy.
	/// </summary>
	[Fact]
	public async Task TenantDashboard_AsTenantAdmin_Returns200WithCorrectShape()
	{
		var response = await _tenantAdminClient.GetAsync("/dashboard/tenant");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<TenantDashboardResponse>();
		Assert.NotNull(body);
		// The response must be scoped to the caller's tenancy.
		Assert.Equal(_tenancyId, body.TenancyId);
		Assert.Equal("DashTenancy", body.TenancyName);
		// The seeded tenancy has tadmin + tuser = 2 users.
		Assert.Equal(2, body.UserCount);
		Assert.NotNull(body.PrivateSubnetUtilisation);
		Assert.NotNull(body.SubnetsApproachingExhaustion);
		Assert.NotNull(body.RecentAuditEntries);
	}

	/// <summary>
	/// Verifies that a GlobalAdmin receives 403 Forbidden from GET /dashboard/tenant.
	/// </summary>
	[Fact]
	public async Task TenantDashboard_AsGlobalAdmin_Returns403()
	{
		var response = await _adminClient.GetAsync("/dashboard/tenant");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantUser receives 403 Forbidden from GET /dashboard/tenant.
	/// </summary>
	[Fact]
	public async Task TenantDashboard_AsTenantUser_Returns403()
	{
		var response = await _tenantUserClient.GetAsync("/dashboard/tenant");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that an unauthenticated request to GET /dashboard/tenant returns 401.
	/// </summary>
	[Fact]
	public async Task TenantDashboard_Unauthenticated_Returns401()
	{
		var client = Factory.CreateClient();

		var response = await client.GetAsync("/dashboard/tenant");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	// ── GET /dashboard/user ───────────────────────────────────────────────────

	/// <summary>
	/// Verifies that a TenantUser receives 200 OK from GET /dashboard/user with
	/// a correctly shaped <see cref="UserDashboardResponse"/>.
	/// </summary>
	[Fact]
	public async Task UserDashboard_AsTenantUser_Returns200WithCorrectShape()
	{
		var response = await _tenantUserClient.GetAsync("/dashboard/user");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<UserDashboardResponse>();
		Assert.NotNull(body);
		// No subnets or allocations are seeded, so both lists are empty but non-null.
		Assert.NotNull(body.RecentAccessibleAllocations);
		Assert.NotNull(body.AccessibleSubnets);
	}

	/// <summary>
	/// Verifies that a GlobalAdmin receives 403 Forbidden from GET /dashboard/user.
	/// </summary>
	[Fact]
	public async Task UserDashboard_AsGlobalAdmin_Returns403()
	{
		var response = await _adminClient.GetAsync("/dashboard/user");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantAdmin receives 403 Forbidden from GET /dashboard/user.
	/// </summary>
	[Fact]
	public async Task UserDashboard_AsTenantAdmin_Returns403()
	{
		var response = await _tenantAdminClient.GetAsync("/dashboard/user");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that an unauthenticated request to GET /dashboard/user returns 401.
	/// </summary>
	[Fact]
	public async Task UserDashboard_Unauthenticated_Returns401()
	{
		var client = Factory.CreateClient();

		var response = await client.GetAsync("/dashboard/user");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="DashboardControllerTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class DashboardControllerTests : DashboardControllerTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public DashboardControllerTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="DashboardControllerTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class DashboardControllerMySqlTests : DashboardControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public DashboardControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="DashboardControllerTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class DashboardControllerPostgresTests : DashboardControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public DashboardControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
