using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

public abstract class PrivateSubnetsAndExclusionsControllerTestsBase : IAsyncLifetime
{
	protected readonly TestWebApplicationFactory Factory;
	private HttpClient _adminClient = null!;
	private HttpClient _tenantAdminClient = null!;
	private HttpClient _otherTenantAdminClient = null!;
	private Guid _tenancyId;
	private Guid _otherTenancyId;

	protected PrivateSubnetsAndExclusionsControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			var admin = new ApplicationUser { UserName = "admin", Email = "admin", Role = "GlobalAdmin" };
			await um.CreateAsync(admin, "Test1234!");

			var tenancy = new Tenancy { Id = Guid.NewGuid(), Name = "TenancyA", Description = "", CreatedAt = DateTime.UtcNow };
			var otherTenancy = new Tenancy { Id = Guid.NewGuid(), Name = "TenancyB", Description = "", CreatedAt = DateTime.UtcNow };
			_tenancyId = tenancy.Id;
			_otherTenancyId = otherTenancy.Id;
			db.Tenancies.AddRange(tenancy, otherTenancy);

			var tadmin = new ApplicationUser
			{
				UserName = "tadmin",
				Email = "tadmin",
				Role = "TenantAdmin",
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(tadmin, "Test1234!");

			var otherAdmin = new ApplicationUser
			{
				UserName = "otheradmin",
				Email = "otheradmin",
				Role = "TenantAdmin",
				TenancyId = otherTenancy.Id
			};
			await um.CreateAsync(otherAdmin, "Test1234!");

			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
		_tenantAdminClient = Factory.CreateAuthenticatedClient("tadmin", "Test1234!");
		_otherTenantAdminClient = Factory.CreateAuthenticatedClient("otheradmin", "Test1234!");
	}

	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	[Fact]
	public async Task UpdatePrivateSubnet_AsTenantAdmin_ReturnsOk()
	{
		var createReq = new CreateSubnetRequest("10.2.0.0/24", "Before", "BeforeDesc");
		var createResp = await _tenantAdminClient.PostAsJsonAsync($"/api/tenancies/{_tenancyId}/subnets", createReq);
		var subnet = await createResp.Content.ReadFromJsonAsync<SubnetResponse>();

		var updateReq = new UpdateSubnetRequest("After", "AfterDesc");
		var updateResp = await _tenantAdminClient.PutAsJsonAsync($"/api/tenancies/{_tenancyId}/subnets/{subnet!.Id}", updateReq);
		Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

		var updated = await updateResp.Content.ReadFromJsonAsync<SubnetResponse>();
		Assert.Equal("After", updated!.Name);
		Assert.Equal("AfterDesc", updated.Description);
		Assert.Equal("10.2.0.0/24", updated.Cidr);
	}

	[Fact]
	public async Task UpdatePrivateSubnet_CrossTenancy_ReturnsForbidden()
	{
		var createReq = new CreateSubnetRequest("10.3.0.0/24", "Before", "BeforeDesc");
		var createResp = await _tenantAdminClient.PostAsJsonAsync($"/api/tenancies/{_tenancyId}/subnets", createReq);
		var subnet = await createResp.Content.ReadFromJsonAsync<SubnetResponse>();

		var updateReq = new UpdateSubnetRequest("After", "AfterDesc");
		var updateResp = await _otherTenantAdminClient.PutAsJsonAsync($"/api/tenancies/{_tenancyId}/subnets/{subnet!.Id}", updateReq);
		Assert.Equal(HttpStatusCode.Forbidden, updateResp.StatusCode);
	}

	[Fact]
	public async Task UpdateExclusionDescription_AsTenantAdmin_ReturnsOk()
	{
		var createSubnetResp = await _tenantAdminClient.PostAsJsonAsync(
			$"/api/tenancies/{_tenancyId}/subnets",
			new CreateSubnetRequest("10.4.0.0/24", "Subnet", "Desc"));
		var subnet = await createSubnetResp.Content.ReadFromJsonAsync<SubnetResponse>();

		var createExclResp = await _tenantAdminClient.PostAsJsonAsync(
			$"/api/subnets/{subnet!.Id}/exclusions",
			new CreateExclusionRequest("10.4.0.1", "10.4.0.1", "Gateway"));
		var exclusion = await createExclResp.Content.ReadFromJsonAsync<ExclusionResponse>();

		var updateResp = await _tenantAdminClient.PutAsJsonAsync(
			$"/api/subnets/{subnet.Id}/exclusions/{exclusion!.Id}",
			new UpdateExclusionRequest("Updated gateway"));
		Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

		var updated = await updateResp.Content.ReadFromJsonAsync<ExclusionResponse>();
		Assert.Equal("Updated gateway", updated!.Description);
		Assert.Equal("10.4.0.1", updated.Start);
		Assert.Equal("10.4.0.1", updated.End);
	}
}

public class PrivateSubnetsAndExclusionsControllerTests : PrivateSubnetsAndExclusionsControllerTestsBase
{
	public PrivateSubnetsAndExclusionsControllerTests() : base(new TestWebApplicationFactory()) { }
}

[Collection("mysql")]
public class PrivateSubnetsAndExclusionsControllerMySqlTests : PrivateSubnetsAndExclusionsControllerTestsBase
{
	public PrivateSubnetsAndExclusionsControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

[Collection("postgres")]
public class PrivateSubnetsAndExclusionsControllerPostgresTests : PrivateSubnetsAndExclusionsControllerTestsBase
{
	public PrivateSubnetsAndExclusionsControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
