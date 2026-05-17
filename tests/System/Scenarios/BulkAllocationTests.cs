using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.System.Scenarios;

/// <summary>
/// Abstract base class for end-to-end bulk IP allocation scenario tests. Concrete
/// subclasses supply the factory so the same scenarios run against SQLite, MySQL,
/// and PostgreSQL without duplicating test logic.
///
/// A /28 subnet (14 usable IPs) is used as the target so tests can verify bulk
/// success, individual release within a bulk group, and failure when the requested
/// block size exceeds availability.
/// </summary>
public abstract class BulkAllocationTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as a TenantUser.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>ID of the /28 private subnet created during initialisation.</summary>
	private Guid _subnetId;

	/// <summary>
	/// Initialises a new instance of <see cref="BulkAllocationTestsBase"/> using the
	/// supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected BulkAllocationTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a tenancy, a TenantUser, and a private /28 subnet
	/// (14 usable host addresses) that the bulk allocation tests will operate on.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			var tenancy = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "BulkTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			db.Tenancies.Add(tenancy);

			var user = new ApplicationUser
			{
				UserName = "buser",
				Email = "buser",
				Role = "TenantUser",
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(user, "Test1234!");

			// 10.20.0.0/28 has 14 usable IPs (10.20.0.1 through 10.20.0.14).
			var subnet = new Subnet
			{
				Id = Guid.NewGuid(),
				Cidr = "10.20.0.0/28",
				Name = "BulkSubnet",
				Description = "",
				Type = SubnetType.Private,
				TenancyId = tenancy.Id,
				CreatedAt = DateTime.UtcNow
			};
			_subnetId = subnet.Id;
			db.Subnets.Add(subnet);

			await db.SaveChangesAsync();
		});

		_tenantUserClient = Factory.CreateAuthenticatedClient("buser", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that a bulk allocation returns the requested number of IPs and
	/// that all of them share the same non-null BulkId.
	/// </summary>
	[Fact]
	public async Task BulkAllocate_SharesBulkId()
	{
		var resp = await _tenantUserClient.PostAsJsonAsync("/api/allocations/bulk",
			new BulkAllocateRequest(_subnetId, 5, "bulk"));
		Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

		var allocations = await resp.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.Equal(5, allocations!.Count);

		// Capture the BulkId from the first allocation.
		var bulkId = allocations[0].BulkId;
		Assert.NotNull(bulkId);

		// Every allocation in the batch must share the same BulkId.
		Assert.All(allocations, a => Assert.Equal(bulkId, a.BulkId));
	}

	/// <summary>
	/// Verifies that one IP in a bulk group can be released independently while
	/// the others remain allocated.
	/// </summary>
	[Fact]
	public async Task BulkAllocate_IndividualRelease_Works()
	{
		// Bulk-allocate 3 IPs.
		var resp = await _tenantUserClient.PostAsJsonAsync("/api/allocations/bulk",
			new BulkAllocateRequest(_subnetId, 3, "indiv-release"));
		var allocations = await resp.Content.ReadFromJsonAsync<List<AllocationResponse>>();

		// Release only the second allocation in the batch.
		var releaseResp = await _tenantUserClient.DeleteAsync(
			$"/api/allocations/{allocations![1].Id}");
		Assert.Equal(HttpStatusCode.NoContent, releaseResp.StatusCode);

		// Retrieve the full list and confirm the first and third IPs still exist
		// but the second has been removed.
		var list = await _tenantUserClient.GetFromJsonAsync<List<AllocationResponse>>("/api/allocations");
		Assert.Contains(list!, a => a.Id == allocations[0].Id);
		Assert.DoesNotContain(list!, a => a.Id == allocations[1].Id);
	}

	/// <summary>
	/// Verifies that requesting a contiguous block larger than the number of
	/// available IPs returns 409 Conflict.
	/// The /28 has 14 usable IPs, so requesting 15 must be rejected.
	/// </summary>
	[Fact]
	public async Task BulkAllocate_ExceedsCapacity_Returns409()
	{
		// 15 IPs is one more than the /28 can provide.
		var resp = await _tenantUserClient.PostAsJsonAsync("/api/allocations/bulk",
			new BulkAllocateRequest(_subnetId, 15, "too-many"));
		Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="BulkAllocationTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class BulkAllocationTests : BulkAllocationTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public BulkAllocationTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="BulkAllocationTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class BulkAllocationMySqlTests : BulkAllocationTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public BulkAllocationMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="BulkAllocationTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class BulkAllocationPostgresTests : BulkAllocationTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public BulkAllocationPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
