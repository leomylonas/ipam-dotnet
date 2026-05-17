using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.System.Scenarios;

/// <summary>
/// Abstract base class for the end-to-end tenancy lifecycle scenario. Concrete
/// subclasses supply the factory so the full flow is exercised against SQLite,
/// MySQL, and PostgreSQL without duplicating the test logic.
///
/// The scenario exercises multiple controllers in sequence (Tenancies → Users →
/// PrivateSubnets → Allocations → Audit) to confirm that the full flow works as
/// a connected system rather than in isolation.
/// </summary>
public abstract class TenancyLifecycleTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>
	/// Initialises a new instance of <see cref="TenancyLifecycleTestsBase"/> using
	/// the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected TenancyLifecycleTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds only the GlobalAdmin user. All other data is created by the test
	/// itself via HTTP to exercise the full end-to-end flow.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = "GlobalAdmin"
			};
			await um.CreateAsync(admin, "Test1234!");
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Full lifecycle scenario:
	/// <list type="number">
	///   <item>GlobalAdmin creates a tenancy (which also creates a TenantAdmin).</item>
	///   <item>GlobalAdmin creates a TenantUser in that tenancy.</item>
	///   <item>GlobalAdmin creates a private subnet in the tenancy.</item>
	///   <item>TenantUser allocates an IP from the subnet.</item>
	///   <item>TenantUser releases the allocation.</item>
	///   <item>TenantAdmin reads the audit log and confirms both events are recorded.</item>
	/// </list>
	/// </summary>
	[Fact]
	public async Task FullLifecycle_CreateTenancy_AllocateIp_Release_CheckAudit()
	{
		// ── Step 1: Create tenancy ──────────────────────────────────────────────
		var tenancyResp = await _adminClient.PostAsJsonAsync("/api/tenancies",
			new CreateTenancyRequest("LifecycleTenancy", "Desc", "ltadmin", "Test1234!"));
		Assert.Equal(HttpStatusCode.Created, tenancyResp.StatusCode);
		var tenancy = await tenancyResp.Content.ReadFromJsonAsync<TenancyResponse>();

		// ── Step 2: Create TenantUser via GlobalAdmin ───────────────────────────
		var userResp = await _adminClient.PostAsJsonAsync("/api/users",
			new CreateUserRequest("ltuser", "Test1234!", "TenantUser", tenancy!.Id));
		Assert.Equal(HttpStatusCode.Created, userResp.StatusCode);

		// ── Step 3: Create private subnet as GlobalAdmin ────────────────────────
		var subnetResp = await _adminClient.PostAsJsonAsync(
			$"/api/tenancies/{tenancy.Id}/subnets",
			new CreateSubnetRequest("10.1.1.0/28", "LifecycleSubnet", ""));
		Assert.Equal(HttpStatusCode.Created, subnetResp.StatusCode);
		var subnet = await subnetResp.Content.ReadFromJsonAsync<SubnetResponse>();

		// ── Step 4: Allocate IP as TenantUser ──────────────────────────────────
		// Create a fresh client authenticated as the TenantUser we just created.
		var userClient = Factory.CreateAuthenticatedClient("ltuser", "Test1234!");
		var allocResp = await userClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(subnet!.Id, "lifecycle test"));
		Assert.Equal(HttpStatusCode.Created, allocResp.StatusCode);
		var allocation = await allocResp.Content.ReadFromJsonAsync<AllocationResponse>();

		// ── Step 5: Release allocation ──────────────────────────────────────────
		var releaseResp = await userClient.DeleteAsync($"/api/allocations/{allocation!.Id}");
		Assert.Equal(HttpStatusCode.NoContent, releaseResp.StatusCode);

		// ── Step 6: Verify audit log as TenantAdmin ─────────────────────────────
		// The TenantAdmin created in step 1 (username: "ltadmin") can read the audit log.
		var tadminClient = Factory.CreateAuthenticatedClient("ltadmin", "Test1234!");
		var auditResp = await tadminClient.GetAsync("/api/audit");
		Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

		var auditLogs = await auditResp.Content.ReadFromJsonAsync<List<AuditLogResponse>>();

		// Both the allocation event and the release event must be present.
		Assert.Contains(auditLogs!, a => a.Action == "Allocated");
		Assert.Contains(auditLogs!, a => a.Action == "Released");
	}
}

/// <summary>
/// Runs <see cref="TenancyLifecycleTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class TenancyLifecycleTests : TenancyLifecycleTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public TenancyLifecycleTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="TenancyLifecycleTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class TenancyLifecycleMySqlTests : TenancyLifecycleTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public TenancyLifecycleMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="TenancyLifecycleTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class TenancyLifecyclePostgresTests : TenancyLifecycleTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public TenancyLifecyclePostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
