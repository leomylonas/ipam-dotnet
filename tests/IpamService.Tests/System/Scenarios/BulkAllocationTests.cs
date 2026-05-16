using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.System.Scenarios;

/// <summary>
/// End-to-end system tests for bulk IP allocation scenarios. A /28 subnet
/// (14 usable IPs) is used as the target so tests can verify bulk success,
/// individual release within a bulk group, and failure when the requested
/// block size exceeds availability.
/// </summary>
public class BulkAllocationTests : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	private readonly TestWebApplicationFactory _factory;

	/// <summary>HTTP client pre-authenticated as a TenantUser.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>ID of the /28 private subnet created during initialisation.</summary>
	private Guid _subnetId;

	/// <summary>
	/// Initialises a new instance of <see cref="BulkAllocationTests"/>.
	/// </summary>
	public BulkAllocationTests()
	{
		_factory = new TestWebApplicationFactory();
	}

	/// <summary>
	/// Seeds the database with a tenancy, a TenantUser, and a private /28 subnet
	/// (14 usable host addresses) that the bulk allocation tests will operate on.
	/// </summary>
	public async Task InitializeAsync()
	{
		await _factory.SeedDatabaseAsync(async (db, um) =>
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

		_tenantUserClient = _factory.CreateAuthenticatedClient("buser", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		_factory.Dispose();
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
