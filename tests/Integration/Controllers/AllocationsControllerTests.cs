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
