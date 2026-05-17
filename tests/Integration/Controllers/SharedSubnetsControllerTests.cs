using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Abstract base class containing all integration tests for <c>SharedSubnetsController</c>.
/// Concrete subclasses supply the factory so the same test logic runs against SQLite,
/// MySQL, and PostgreSQL without duplication.
///
/// Tests cover: GlobalAdmin can create/list shared subnets, TenantUser is forbidden
/// from creating them, and the access grant/revoke lifecycle works correctly.
/// </summary>
public abstract class SharedSubnetsControllerTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>HTTP client pre-authenticated as a TenantUser.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>ID of the tenancy created during initialisation.</summary>
	private Guid _tenancyId;

	/// <summary>
	/// Initialises a new instance of <see cref="SharedSubnetsControllerTestsBase"/>
	/// using the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected SharedSubnetsControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds a GlobalAdmin, a tenancy, and a TenantUser; creates authenticated clients.
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
				Name = "T1",
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
	/// Verifies that GlobalAdmin can create a shared subnet and receives 201 Created.
	/// </summary>
	[Fact]
	public async Task CreateSharedSubnet_ReturnsCreated()
	{
		var req = new CreateSubnetRequest("172.16.0.0/24", "Shared1", "");
		var response = await _adminClient.PostAsJsonAsync("/api/subnets/shared", req);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a TenantUser attempting to create a shared subnet receives 403
	/// Forbidden because only GlobalAdmin is permitted.
	/// </summary>
	[Fact]
	public async Task CreateSharedSubnet_AsTenantUser_Returns403()
	{
		var req = new CreateSubnetRequest("172.16.1.0/24", "Shared2", "");
		var response = await _tenantUserClient.PostAsJsonAsync("/api/subnets/shared", req);
		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	/// <summary>
	/// Verifies that listing shared subnets as an authenticated TenantUser returns
	/// 200 OK (tenant users are allowed to see open shared subnets).
	/// </summary>
	[Fact]
	public async Task ListSharedSubnets_ReturnsOk()
	{
		var response = await _tenantUserClient.GetAsync("/api/subnets/shared");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	/// <summary>
	/// Verifies the complete grant-and-revoke lifecycle for tenancy-level access
	/// restrictions on a shared subnet. Both operations should return 204 No Content.
	/// </summary>
	[Fact]
	public async Task GrantAndRevokeAccess_Works()
	{
		// Create the subnet to apply access restrictions to.
		var createReq = new CreateSubnetRequest("172.16.2.0/24", "AccessTest", "");
		var createResp = await _adminClient.PostAsJsonAsync("/api/subnets/shared", createReq);
		var subnet = await createResp.Content.ReadFromJsonAsync<SubnetResponse>();

		// Grant access to the test tenancy — should succeed.
		var grantResp = await _adminClient.PostAsJsonAsync(
			$"/api/subnets/shared/{subnet!.Id}/access",
			new GrantSubnetAccessRequest(_tenancyId));
		Assert.Equal(HttpStatusCode.NoContent, grantResp.StatusCode);

		// Revoke the access grant — should succeed.
		var revokeResp = await _adminClient.DeleteAsync(
			$"/api/subnets/shared/{subnet.Id}/access/{_tenancyId}");
		Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="SharedSubnetsControllerTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class SharedSubnetsControllerTests : SharedSubnetsControllerTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public SharedSubnetsControllerTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="SharedSubnetsControllerTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class SharedSubnetsControllerMySqlTests : SharedSubnetsControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public SharedSubnetsControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="SharedSubnetsControllerTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class SharedSubnetsControllerPostgresTests : SharedSubnetsControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public SharedSubnetsControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
