using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.System.Scenarios;

/// <summary>
/// End-to-end system tests for the allocation tag system. Tests verify that tags
/// can be set and filtered correctly, that a PUT replaces all existing tags (full
/// replace semantics), and that individual tags can be deleted by key.
/// </summary>
public class TagFilteringTests : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	private readonly TestWebApplicationFactory _factory;

	/// <summary>HTTP client pre-authenticated as a TenantUser.</summary>
	private HttpClient _tenantUserClient = null!;

	/// <summary>ID of the private subnet created during initialisation.</summary>
	private Guid _subnetId;

	/// <summary>
	/// Initialises a new instance of <see cref="TagFilteringTests"/>.
	/// </summary>
	public TagFilteringTests()
	{
		_factory = new TestWebApplicationFactory();
	}

	/// <summary>
	/// Seeds the database with a tenancy, a TenantUser, and a large private subnet
	/// that tests can allocate from without running out of IPs.
	/// </summary>
	public async Task InitializeAsync()
	{
		await _factory.SeedDatabaseAsync(async (db, um) =>
		{
			var tenancy = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TagTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			db.Tenancies.Add(tenancy);

			var user = new ApplicationUser
			{
				UserName = "taguser",
				Email = "taguser",
				Role = "TenantUser",
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(user, "Test1234!");

			// /24 provides 254 usable IPs — more than enough for these tests.
			var subnet = new Subnet
			{
				Id = Guid.NewGuid(),
				Cidr = "10.30.0.0/24",
				Name = "TagSubnet",
				Description = "",
				Type = SubnetType.Private,
				TenancyId = tenancy.Id,
				CreatedAt = DateTime.UtcNow
			};
			_subnetId = subnet.Id;
			db.Subnets.Add(subnet);
			await db.SaveChangesAsync();
		});

		_tenantUserClient = _factory.CreateAuthenticatedClient("taguser", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		_factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that allocations tagged with different values for the same key can
	/// be filtered by both key and value via the query string on GET /api/allocations.
	/// </summary>
	[Fact]
	public async Task AllocateAndTag_FilterByTag_ReturnsCorrect()
	{
		// Allocate two IPs that will receive different tag values.
		var alloc1Resp = await _tenantUserClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(_subnetId, "alloc1"));
		var alloc2Resp = await _tenantUserClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(_subnetId, "alloc2"));
		var alloc1 = await alloc1Resp.Content.ReadFromJsonAsync<AllocationResponse>();
		var alloc2 = await alloc2Resp.Content.ReadFromJsonAsync<AllocationResponse>();

		// Tag alloc1 with env=prod and alloc2 with env=dev.
		await _tenantUserClient.PutAsJsonAsync(
			$"/api/allocations/{alloc1!.Id}/tags",
			new Dictionary<string, string> { ["env"] = "prod" });
		await _tenantUserClient.PutAsJsonAsync(
			$"/api/allocations/{alloc2!.Id}/tags",
			new Dictionary<string, string> { ["env"] = "dev" });

		// Filter by env=prod — should return only alloc1.
		var filtered = await _tenantUserClient.GetFromJsonAsync<List<AllocationResponse>>(
			"/api/allocations?tagKey=env&tagValue=prod");
		Assert.Single(filtered!);
		Assert.Equal(alloc1.Id, filtered![0].Id);
	}

	/// <summary>
	/// Verifies that a PUT to /api/allocations/{id}/tags fully replaces all existing
	/// tags, so after two successive PUTs only the tags from the second PUT remain.
	/// </summary>
	[Fact]
	public async Task PutTags_FullReplace_Works()
	{
		// Allocate an IP to tag.
		var allocResp = await _tenantUserClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(_subnetId, "tag-replace"));
		var alloc = await allocResp.Content.ReadFromJsonAsync<AllocationResponse>();

		// First PUT: set two tags (a=1, b=2).
		await _tenantUserClient.PutAsJsonAsync(
			$"/api/allocations/{alloc!.Id}/tags",
			new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

		// Second PUT: replace with a single different tag (c=3).
		// After this, only c=3 should exist; a and b should be gone.
		await _tenantUserClient.PutAsJsonAsync(
			$"/api/allocations/{alloc.Id}/tags",
			new Dictionary<string, string> { ["c"] = "3" });

		var tags = await _tenantUserClient.GetFromJsonAsync<List<TagResponse>>(
			$"/api/allocations/{alloc.Id}/tags");

		// Only the tag from the second PUT should remain.
		Assert.Single(tags!);
		Assert.Equal("c", tags![0].Key);
	}

	/// <summary>
	/// Verifies that DELETE /api/allocations/{id}/tags/{key} removes exactly the
	/// named tag and leaves all other tags on the allocation intact.
	/// </summary>
	[Fact]
	public async Task DeleteTag_RemovesSingleTag()
	{
		// Allocate an IP and set two tags.
		var allocResp = await _tenantUserClient.PostAsJsonAsync("/api/allocations",
			new AllocateRequest(_subnetId, "del-tag"));
		var alloc = await allocResp.Content.ReadFromJsonAsync<AllocationResponse>();

		await _tenantUserClient.PutAsJsonAsync(
			$"/api/allocations/{alloc!.Id}/tags",
			new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" });

		// Delete only the "x" tag.
		var deleteResp = await _tenantUserClient.DeleteAsync(
			$"/api/allocations/{alloc.Id}/tags/x");
		Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

		// Only the "y" tag should remain.
		var tags = await _tenantUserClient.GetFromJsonAsync<List<TagResponse>>(
			$"/api/allocations/{alloc.Id}/tags");
		Assert.Single(tags!);
		Assert.Equal("y", tags![0].Key);
	}
}
