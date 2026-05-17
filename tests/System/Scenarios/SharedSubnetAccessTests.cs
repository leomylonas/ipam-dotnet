using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.System.Scenarios;

/// <summary>
/// Abstract base class for end-to-end shared subnet tenancy access control scenario
/// tests. Concrete subclasses supply the factory so the same scenarios run against
/// SQLite, MySQL, and PostgreSQL without duplicating test logic.
///
/// Two tenancies (A and B) are created; tests verify that restricting a shared subnet
/// to TenancyA prevents TenancyB from seeing or allocating from it, and that granting
/// access to TenancyB subsequently allows allocation.
/// </summary>
public abstract class SharedSubnetAccessTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantUser in TenancyA.</summary>
	private HttpClient _tenantAClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantUser in TenancyB.</summary>
	private HttpClient _tenantBClient = null!;

	/// <summary>ID of TenancyA, used when granting access restrictions.</summary>
	private Guid _tenancyAId;

	/// <summary>ID of TenancyB, used when granting access and verifying denial.</summary>
	private Guid _tenancyBId;

	/// <summary>
	/// Initialises a new instance of <see cref="SharedSubnetAccessTestsBase"/> using
	/// the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected SharedSubnetAccessTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin, two tenancies (A and B), and one
	/// TenantUser per tenancy. Creates authenticated clients for all three roles.
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

			var tenancyA = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TenancyA",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			_tenancyAId = tenancyA.Id;

			var tenancyB = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TenancyB",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			_tenancyBId = tenancyB.Id;

			db.Tenancies.AddRange(tenancyA, tenancyB);

			var userA = new ApplicationUser
			{
				UserName = "userA",
				Email = "userA",
				Role = "TenantUser",
				TenancyId = tenancyA.Id
			};
			var userB = new ApplicationUser
			{
				UserName = "userB",
				Email = "userB",
				Role = "TenantUser",
				TenancyId = tenancyB.Id
			};
			await um.CreateAsync(userA, "Test1234!");
			await um.CreateAsync(userB, "Test1234!");

			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
		_tenantAClient = Factory.CreateAuthenticatedClient("userA", "Test1234!");
		_tenantBClient = Factory.CreateAuthenticatedClient("userB", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that when a shared subnet is restricted to TenancyA:
	/// <list type="bullet">
	///   <item>TenancyA can see it in the subnet list.</item>
	///   <item>TenancyB cannot see it in the subnet list.</item>
	///   <item>TenancyB receives 403 when attempting to allocate from it.</item>
	/// </list>
	/// </summary>
	[Fact]
	public async Task SharedSubnet_RestrictedToA_BCannotAllocate()
	{
		// Create an unrestricted shared subnet.
		var createResp = await _adminClient.PostAsJsonAsync("/api/subnets/shared",
			new CreateSubnetRequest("172.30.0.0/24", "Restricted", ""));
		var subnet = await createResp.Content.ReadFromJsonAsync<SubnetResponse>();

		// Restrict the subnet to TenancyA — this creates an access restriction row,
		// which means only explicitly listed tenancies can now access the subnet.
		await _adminClient.PostAsJsonAsync(
			$"/api/subnets/shared/{subnet!.Id}/access",
			new GrantSubnetAccessRequest(_tenancyAId));

		// TenancyA should be able to see the subnet in the list.
		var listA = await _tenantAClient.GetFromJsonAsync<List<SubnetResponse>>("/api/subnets/shared");
		Assert.Contains(listA!, s => s.Id == subnet.Id);

		// TenancyB should NOT see the subnet because it is not in the access list.
		var listB = await _tenantBClient.GetFromJsonAsync<List<SubnetResponse>>("/api/subnets/shared");
		Assert.DoesNotContain(listB!, s => s.Id == subnet.Id);

		// TenancyB should receive 403 when attempting to allocate from the restricted subnet.
		var allocResp = await _tenantBClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(subnet.Id, "should fail"));
		Assert.Equal(HttpStatusCode.Forbidden, allocResp.StatusCode);
	}

	/// <summary>
	/// Verifies that after TenancyB is explicitly granted access to a restricted
	/// shared subnet, it can successfully allocate from it.
	/// </summary>
	[Fact]
	public async Task SharedSubnet_GrantAccess_AllowsAllocation()
	{
		// Create and immediately restrict to TenancyA.
		var createResp = await _adminClient.PostAsJsonAsync("/api/subnets/shared",
			new CreateSubnetRequest("172.31.0.0/24", "OpenThenRestrict", ""));
		var subnet = await createResp.Content.ReadFromJsonAsync<SubnetResponse>();

		// Restrict to TenancyA first.
		await _adminClient.PostAsJsonAsync(
			$"/api/subnets/shared/{subnet!.Id}/access",
			new GrantSubnetAccessRequest(_tenancyAId));

		// Now also grant access to TenancyB.
		await _adminClient.PostAsJsonAsync(
			$"/api/subnets/shared/{subnet.Id}/access",
			new GrantSubnetAccessRequest(_tenancyBId));

		// TenancyB should now be able to allocate from the subnet.
		var allocResp = await _tenantBClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(subnet.Id, "now allowed"));
		Assert.Equal(HttpStatusCode.Created, allocResp.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="SharedSubnetAccessTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class SharedSubnetAccessTests : SharedSubnetAccessTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public SharedSubnetAccessTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="SharedSubnetAccessTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class SharedSubnetAccessMySqlTests : SharedSubnetAccessTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public SharedSubnetAccessMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="SharedSubnetAccessTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class SharedSubnetAccessPostgresTests : SharedSubnetAccessTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public SharedSubnetAccessPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
