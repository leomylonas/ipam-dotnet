using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.System.Scenarios;

/// <summary>
/// Abstract base class for end-to-end allocation isolation scenario tests. Concrete
/// subclasses supply the factory so the same scenarios run against SQLite, MySQL, and
/// PostgreSQL without duplicating test logic.
///
/// These tests verify that tenancy isolation is enforced via subnet ownership after the
/// removal of <c>TenancyId</c> from <c>Allocation</c>. Two separate tenancies each own
/// a private subnet. Tests confirm that users from one tenancy cannot see or release
/// allocations from the other tenancy's subnet.
/// </summary>
public abstract class AllocationIsolationTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as a TenantUser in tenancy A.</summary>
	private HttpClient _tenancyAClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantUser in tenancy B.</summary>
	private HttpClient _tenancyBClient = null!;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>Private subnet belonging to tenancy A (10.0.1.0/29).</summary>
	private Guid _subnetAId;

	/// <summary>Private subnet belonging to tenancy B (10.0.2.0/29).</summary>
	private Guid _subnetBId;

	/// <summary>
	/// Initialises a new instance of <see cref="AllocationIsolationTestsBase"/>
	/// using the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected AllocationIsolationTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with two tenancies, a TenantUser per tenancy, a GlobalAdmin,
	/// and one private /29 subnet per tenancy for allocation tests.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			// Create GlobalAdmin.
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = "GlobalAdmin"
			};
			await um.CreateAsync(admin, "Test1234!");

			// Create tenancy A.
			var tenancyA = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TenancyA",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			db.Tenancies.Add(tenancyA);

			// Create tenancy B.
			var tenancyB = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TenancyB",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			db.Tenancies.Add(tenancyB);

			// TenantUser for tenancy A.
			var userA = new ApplicationUser
			{
				UserName = "userA",
				Email = "userA",
				Role = "TenantUser",
				TenancyId = tenancyA.Id
			};
			await um.CreateAsync(userA, "Test1234!");

			// TenantUser for tenancy B.
			var userB = new ApplicationUser
			{
				UserName = "userB",
				Email = "userB",
				Role = "TenantUser",
				TenancyId = tenancyB.Id
			};
			await um.CreateAsync(userB, "Test1234!");

			// Private /29 subnet for tenancy A (6 usable IPs: 10.0.1.1 – 10.0.1.6).
			var subnetA = new Subnet
			{
				Id = Guid.NewGuid(),
				Cidr = "10.0.1.0/29",
				Name = "SubnetA",
				Description = "",
				Type = SubnetType.Private,
				TenancyId = tenancyA.Id,
				CreatedAt = DateTime.UtcNow
			};
			_subnetAId = subnetA.Id;
			db.Subnets.Add(subnetA);

			// Private /29 subnet for tenancy B (6 usable IPs: 10.0.2.1 – 10.0.2.6).
			var subnetB = new Subnet
			{
				Id = Guid.NewGuid(),
				Cidr = "10.0.2.0/29",
				Name = "SubnetB",
				Description = "",
				Type = SubnetType.Private,
				TenancyId = tenancyB.Id,
				CreatedAt = DateTime.UtcNow
			};
			_subnetBId = subnetB.Id;
			db.Subnets.Add(subnetB);

			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
		_tenancyAClient = Factory.CreateAuthenticatedClient("userA", "Test1234!");
		_tenancyBClient = Factory.CreateAuthenticatedClient("userB", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that a TenantUser can only list allocations on their own tenancy's subnets.
	/// Allocations created on tenancy B's subnet must not appear in tenancy A's list.
	/// </summary>
	[Fact]
	public async Task TenantUser_CannotSeeOtherTenancyAllocations()
	{
		// Create an allocation on subnet B (tenancy B).
		await _tenancyBClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetBId, "tenancy B alloc"));

		// Create an allocation on subnet A (tenancy A).
		await _tenancyAClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetAId, "tenancy A alloc"));

		// Tenancy A list must only contain the allocation from subnet A.
		var listResp = await _tenancyAClient.GetAsync("/api/allocations");
		Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

		var allocations = await listResp.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.NotNull(allocations);

		// Only 1 allocation — the one on subnet A.
		Assert.Single(allocations!);
		Assert.Equal(_subnetAId, allocations![0].SubnetId);
	}

	/// <summary>
	/// Verifies that a TenantUser cannot allocate from a subnet that belongs to a
	/// different tenancy. The service enforces subnet access; the response must be 403.
	/// </summary>
	[Fact]
	public async Task TenantUser_CannotAllocateFromOtherTenancySubnet()
	{
		// Attempt to allocate from subnet B using tenancy A credentials.
		var req = new AllocateRequest(_subnetBId, "cross-tenancy attempt");
		var response = await _tenancyAClient.PostAsJsonAsync("/api/allocations", req);
		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantUser cannot release an allocation that belongs to a
	/// different tenancy's subnet, even when they know the allocation ID.
	/// </summary>
	[Fact]
	public async Task TenantUser_CannotReleaseOtherTenancyAllocation()
	{
		// Create an allocation on subnet B.
		var createResp = await _tenancyBClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetBId, "B alloc"));
		var allocation = await createResp.Content.ReadFromJsonAsync<AllocationResponse>();

		// Attempt to release it as tenancy A.
		var deleteResp = await _tenancyAClient.DeleteAsync($"/api/allocations/{allocation!.Id}");
		Assert.Equal(HttpStatusCode.Forbidden, deleteResp.StatusCode);
	}

	/// <summary>
	/// Verifies that GlobalAdmin can see all allocations across all tenancies in the
	/// listing endpoint, confirming that subnet-based scoping works correctly.
	/// </summary>
	[Fact]
	public async Task GlobalAdmin_SeesAllAllocationsAcrossTenancies()
	{
		// Allocate from both tenancy subnets.
		await _tenancyAClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetAId, "A alloc"));
		await _tenancyBClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetBId, "B alloc"));

		// GlobalAdmin list must contain both.
		var listResp = await _adminClient.GetAsync("/api/allocations");
		var allocations = await listResp.Content.ReadFromJsonAsync<List<AllocationResponse>>();
		Assert.NotNull(allocations);
		Assert.Equal(2, allocations!.Count);

		// Both subnet IDs must be represented.
		var subnetIds = allocations!.Select(a => a.SubnetId).ToHashSet();
		Assert.Contains(_subnetAId, subnetIds);
		Assert.Contains(_subnetBId, subnetIds);
	}

	/// <summary>
	/// Verifies that GlobalAdmin can release an allocation from any tenancy's subnet.
	/// </summary>
	[Fact]
	public async Task GlobalAdmin_CanReleaseAnyTenancyAllocation()
	{
		// Create an allocation on tenancy A's subnet.
		var createResp = await _tenancyAClient.PostAsJsonAsync("/api/allocations", new AllocateRequest(_subnetAId, "A alloc"));
		var allocation = await createResp.Content.ReadFromJsonAsync<AllocationResponse>();

		// Admin releases it.
		var deleteResp = await _adminClient.DeleteAsync($"/api/allocations/{allocation!.Id}");
		Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="AllocationIsolationTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class AllocationIsolationTests : AllocationIsolationTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public AllocationIsolationTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="AllocationIsolationTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class AllocationIsolationMySqlTests : AllocationIsolationTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public AllocationIsolationMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="AllocationIsolationTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class AllocationIsolationPostgresTests : AllocationIsolationTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public AllocationIsolationPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
