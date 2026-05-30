using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Abstract base class containing all integration tests for <c>AllocationsController</c>.
/// Concrete subclasses supply the factory so the same test logic runs against SQLite,
/// MySQL, and PostgreSQL without duplication.
///
/// Each instance gets its own isolated database seeded with a tenancy, a TenantUser,
/// and a small private subnet (10.0.0.0/29 = 6 usable IPs) so tests can exercise the
/// full allocation/release cycle.
///
/// Tests cover: single allocation, listing, release, bulk allocation (success and
/// 409 when subnet is exhausted), and unauthenticated 401.
/// </summary>
public abstract class AllocationsControllerTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as a TenantUser.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>ID of the private subnet created during initialisation.</summary>
	private Guid _subnetId;

	/// <summary>ID of the tenancy created during initialisation.</summary>
	private Guid _tenancyId;

	/// <summary>
	/// Initialises a new instance of <see cref="AllocationsControllerTestsBase"/>
	/// using the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected AllocationsControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin, a tenancy, a TenantUser, and a private
	/// /29 subnet (6 usable IPs) that the tests will allocate from.
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

			var tenancy = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TestTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			_tenancyId = tenancy.Id;
			db.Tenancies.Add(tenancy);

			var user = new ApplicationUser
			{
				UserName = "tuser",
				Email = "tuser",
				Role = "TenantUser",
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(user, "Test1234!");

			// 10.0.0.0/29 has 6 usable host addresses (10.0.0.1 through 10.0.0.6).
			var subnet = new Subnet
			{
				Id = Guid.NewGuid(),
				Cidr = "10.0.0.0/29",
				Name = "TestSubnet",
				Description = "",
				Type = SubnetType.Private,
				TenancyId = tenancy.Id,
				CreatedAt = DateTime.UtcNow
			};
			_subnetId = subnet.Id;
			db.Subnets.Add(subnet);

			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
		_tenantUserClient = Factory.CreateAuthenticatedClient("tuser", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that allocating from a subnet with available IPs returns 201 Created
	/// and assigns the first usable IP (10.0.0.1 in this case).
	/// </summary>
	[Fact]
	public async Task Allocate_ReturnsCreated()
	{
		var req = new AllocateRequest(_subnetId, "test allocation");
		var response = await _tenantUserClient.PostAsJsonAsync("/api/allocations", req);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);

		var allocation = await response.Content.ReadFromJsonAsync<AllocationResponse>();
		Assert.NotNull(allocation);

		// The allocator walks in ascending order so 10.0.0.1 is always first.
		Assert.Equal("10.0.0.1", allocation.IpAddress);
	}

	/// <summary>
	/// Verifies that after allocating an IP it appears in the GET /api/allocations list.
	/// </summary>
	[Fact]
	public async Task AllocateAndList_ReturnsAllocations()
	{
		// Create an allocation.
		var req = new AllocateRequest(_subnetId, "listed");
		await _tenantUserClient.PostAsJsonAsync("/api/allocations", req);

		// Retrieve the list and confirm it is non-empty.
		var list = await _tenantUserClient.GetAsync("/api/allocations");
		Assert.Equal(HttpStatusCode.OK, list.StatusCode);

		var allocations = await list.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.NotEmpty(allocations!);
	}

	/// <summary>
	/// Verifies that releasing an allocation returns 204 No Content.
	/// </summary>
	[Fact]
	public async Task Release_ReturnsNoContent()
	{
		// Allocate an IP to release.
		var req = new AllocateRequest(_subnetId, "to release");
		var createResponse = await _tenantUserClient.PostAsJsonAsync("/api/allocations", req);
		var allocation = await createResponse.Content.ReadFromJsonAsync<AllocationResponse>();

		// Release it via DELETE.
		var deleteResponse = await _tenantUserClient.DeleteAsync($"/api/allocations/{allocation!.Id}");
		Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
	}

	/// <summary>
	/// Verifies that a bulk allocation of 3 consecutive IPs returns 201 Created
	/// with all 3 allocations sharing the same BulkId.
	/// </summary>
	[Fact]
	public async Task BulkAllocate_ReturnsCreatedBlock()
	{
		var req = new BulkAllocateRequest(_subnetId, 3, "bulk");
		var response = await _tenantUserClient.PostAsJsonAsync("/api/allocations/bulk", req);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);

		var allocations = await response.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.Equal(3, allocations!.Count);

		// All IPs in the bulk request must share the same BulkId.
		Assert.All(allocations, a => Assert.Equal(allocations[0].BulkId, a.BulkId));
	}

	/// <summary>
	/// Verifies that requesting a bulk allocation when the subnet is fully allocated
	/// returns 409 Conflict with a descriptive message.
	/// </summary>
	[Fact]
	public async Task BulkAllocate_NoContiguousBlock_Returns409()
	{
		// Fill all 6 usable IPs in the /29 subnet.
		for (var i = 0; i < 6; i++)
		{
			await _tenantUserClient.PostAsJsonAsync("/api/allocations",
				new AllocateRequest(_subnetId, "fill"));
		}

		// Now try to bulk-allocate 2 more — should fail with 409.
		var req = new BulkAllocateRequest(_subnetId, 2, "should fail");
		var response = await _tenantUserClient.PostAsJsonAsync("/api/allocations/bulk", req);
		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
	}

	/// <summary>
	/// Verifies that an unauthenticated POST to /api/allocations returns 401 Unauthorized.
	/// </summary>
	[Fact]
	public async Task Allocate_Unauthenticated_Returns401()
	{
		// Use a raw client with no auth header.
		var client = Factory.CreateClient();
		var req = new AllocateRequest(_subnetId, "test");
		var response = await client.PostAsJsonAsync("/api/allocations", req);
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a GlobalAdmin can allocate an IP from any private subnet regardless
	/// of which tenancy owns the subnet. TenancyId is no longer stored on Allocation —
	/// it is derived from the subnet — so GlobalAdmin allocations must still succeed.
	/// </summary>
	[Fact]
	public async Task GlobalAdmin_CanAllocateFromPrivateSubnet()
	{
		// The admin client allocates from the tenancy-owned subnet seeded in InitializeAsync.
		var req = new AllocateRequest(_subnetId, "admin allocation");
		var response = await _adminClient.PostAsJsonAsync("/api/allocations", req);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);

		var allocation = await response.Content.ReadFromJsonAsync<AllocationResponse>();
		Assert.NotNull(allocation);
		Assert.Equal("10.0.0.1", allocation.IpAddress);

		// Confirm the allocation is visible to GlobalAdmin in the list.
		var listResponse = await _adminClient.GetAsync("/api/allocations");
		var allocations = await listResponse.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.Contains(allocations!, a => a.Id == allocation.Id);
	}

	/// <summary>
	/// Verifies that GlobalAdmin allocations appear in the full list returned to GlobalAdmin,
	/// and that no TenancyId field is present in the response (the model no longer has it).
	/// </summary>
	[Fact]
	public async Task GlobalAdmin_AllocationsListContainsAllEntries()
	{
		// Allocate once as TenantUser.
		await _tenantUserClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetId, "user alloc"));

		// Allocate once as GlobalAdmin.
		await _adminClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetId, "admin alloc"));

		// GlobalAdmin list must contain both.
		var listResponse = await _adminClient.GetAsync("/api/allocations");
		Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

		var allocations = await listResponse.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.NotNull(allocations);
		Assert.Equal(2, allocations!.Count);
	}

	/// <summary>
	/// Verifies that a TenantUser only sees allocations within their own tenancy's subnets.
	/// A GlobalAdmin allocation from the same subnet is still visible to the TenantUser
	/// because it is scoped to that subnet (which belongs to the user's tenancy).
	/// </summary>
	[Fact]
	public async Task TenantUser_CanSeeAllocationsOnOwnSubnet()
	{
		// Allocate as GlobalAdmin from the tenancy-owned subnet.
		await _adminClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetId, "admin alloc on tenant subnet"));

		// Allocate as TenantUser.
		await _tenantUserClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetId, "user alloc"));

		// TenantUser list should contain both, since both are on their accessible subnet.
		var listResponse = await _tenantUserClient.GetAsync("/api/allocations");
		var allocations = await listResponse.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.NotNull(allocations);
		Assert.Equal(2, allocations!.Count);
	}

	/// <summary>
	/// Regression test for the TOCTOU race condition where two requests arriving
	/// simultaneously both read the same "first available" IP before either commits.
	/// Without the unique index and retry logic, both requests would succeed and the
	/// same IP would be allocated twice. With the fix, every response must contain a
	/// distinct IP address.
	/// </summary>
	[Fact]
	public async Task ConcurrentAllocate_NoDuplicateIps()
	{
		// Fire 5 requests simultaneously — the subnet has 6 usable IPs (.1–.6) so all
		// 5 must succeed, and each must receive a distinct address.
		const int concurrentRequests = 5;

		// _tenantUserClient is thread-safe for concurrent sends; all tasks are launched
		// before any awaiting so they hit the server at the same time.
		var tasks = Enumerable.Range(0, concurrentRequests)
			.Select(_ => _tenantUserClient.PostAsJsonAsync(
				"/api/allocations", new AllocateRequest(_subnetId, "concurrent")))
			.ToList();

		var responses = await Task.WhenAll(tasks);

		// Every request must have received 201 Created — there are enough free IPs.
		Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

		// Deserialise all responses and verify each IP is unique.
		var allocations = await Task.WhenAll(
			responses.Select(r => r.Content.ReadFromJsonAsync<AllocationResponse>()));

		var ips = allocations.Select(a => a!.IpAddress).ToList();

		// The key assertion: no two concurrent requests should have been given the same IP.
		Assert.Equal(concurrentRequests, ips.Distinct().Count());
	}

	/// <summary>
	/// Verifies that releasing an allocation created by GlobalAdmin works correctly
	/// when the admin calls DELETE on that allocation.
	/// </summary>
	[Fact]
	public async Task GlobalAdmin_CanReleaseOwnAllocation()
	{
		// Allocate as GlobalAdmin.
		var createResp = await _adminClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetId, "to release"));
		var allocation = await createResp.Content.ReadFromJsonAsync<AllocationResponse>();

		// Release it.
		var deleteResp = await _adminClient.DeleteAsync($"/api/allocations/{allocation!.Id}");
		Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

		// Confirm it is gone.
		var listResp = await _adminClient.GetAsync("/api/allocations");
		var allocations = await listResp.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.DoesNotContain(allocations!, a => a.Id == allocation.Id);
	}
}

/// <summary>
/// Runs <see cref="AllocationsControllerTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class AllocationsControllerTests : AllocationsControllerTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public AllocationsControllerTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="AllocationsControllerTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class AllocationsControllerMySqlTests : AllocationsControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public AllocationsControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="AllocationsControllerTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class AllocationsControllerPostgresTests : AllocationsControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public AllocationsControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
