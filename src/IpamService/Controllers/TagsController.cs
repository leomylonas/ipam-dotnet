using System.Security.Claims;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Controllers;

/// <summary>
/// Manages freeform key-value tags on IP allocations. Tags allow callers to
/// annotate allocations with arbitrary metadata (e.g. environment, owner, cost
/// centre) and filter allocations by those tags via the <c>GET /api/allocations</c>
/// endpoint.
///
/// Tag keys must be unique per allocation. A <c>PUT</c> performs a full replace —
/// all existing tags are deleted and the supplied map is inserted — making the
/// operation idempotent.
///
/// Access rules mirror allocation visibility: GlobalAdmin can tag any allocation;
/// TenantAdmin and TenantUser can only tag allocations within their own tenancy.
/// </summary>
[ApiController]
[Route("api/allocations/{id:guid}/tags")]
[Authorize]
public class TagsController : ControllerBase
{
	/// <summary>EF Core context for tag and allocation queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="TagsController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	public TagsController(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>The role of the currently authenticated user.</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>The tenancy ID of the caller, or <c>null</c> for GlobalAdmin.</summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Resolves the allocation and checks whether the caller is permitted to
	/// operate on it. GlobalAdmin can access any allocation; all other roles are
	/// restricted to allocations within their own tenancy.
	/// </summary>
	/// <param name="id">The allocation ID to look up.</param>
	/// <returns>
	/// A tuple of the resolved <see cref="Allocation"/> (or <c>null</c> if not found)
	/// and a boolean indicating whether the caller is forbidden from accessing it.
	/// </returns>
	private async Task<(Allocation? allocation, bool forbidden)> GetAuthorizedAllocationAsync(Guid id)
	{
		var allocation = await _db.Allocations.FindAsync(id);

		// Signal not-found with forbidden = false so the caller returns 404.
		if (allocation is null)
		{
			return (null, false);
		}

		// GlobalAdmin has unrestricted access.
		if (CallerRole == "GlobalAdmin")
		{
			return (allocation, false);
		}

		// Tenant roles may only access allocations belonging to their own tenancy.
		if (allocation.TenancyId != CallerTenancyId)
		{
			return (null, true);
		}

		return (allocation, false);
	}

	/// <summary>
	/// Returns all tags attached to the specified allocation.
	/// </summary>
	/// <param name="id">The ID of the allocation whose tags to list.</param>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="TagResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller cannot access this allocation;
	/// <c>404 Not Found</c> if the allocation does not exist.
	/// </returns>
	[HttpGet]
	public async Task<IActionResult> List(Guid id)
	{
		var (allocation, forbidden) = await GetAuthorizedAllocationAsync(id);
		if (forbidden)
		{
			return Forbid();
		}

		if (allocation is null)
		{
			return NotFound();
		}

		var tags = await _db.AllocationTags
			.Where(t => t.AllocationId == id)
			.Select(t => new TagResponse(t.Id, t.Key, t.Value))
			.ToListAsync();

		return Ok(tags);
	}

	/// <summary>
	/// Fully replaces all tags on the specified allocation with the supplied
	/// key-value map. All existing tags are deleted first, then the new set is
	/// inserted. This makes the operation a safe idempotent PUT.
	/// </summary>
	/// <param name="id">The ID of the allocation whose tags to replace.</param>
	/// <param name="tags">A dictionary of key-value pairs to set as the new tag set.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot access this allocation;
	/// <c>404 Not Found</c> if the allocation does not exist.
	/// </returns>
	[HttpPut]
	public async Task<IActionResult> Replace(Guid id, [FromBody] Dictionary<string, string> tags)
	{
		var (allocation, forbidden) = await GetAuthorizedAllocationAsync(id);
		if (forbidden)
		{
			return Forbid();
		}

		if (allocation is null)
		{
			return NotFound();
		}

		// Delete all existing tags for this allocation in a single SQL DELETE.
		await _db.AllocationTags.Where(t => t.AllocationId == id).ExecuteDeleteAsync();

		// Insert the new tags. The unique index on (AllocationId, Key) enforces that
		// duplicate keys are rejected at the database level, but because the request
		// body is a Dictionary<string, string> the framework already deduplicates keys.
		foreach (var (key, value) in tags)
		{
			_db.AllocationTags.Add(new AllocationTag
			{
				Id = Guid.NewGuid(),
				AllocationId = id,
				Key = key,
				Value = value
			});
		}

		await _db.SaveChangesAsync();
		return NoContent();
	}

	/// <summary>
	/// Deletes a single tag from an allocation, identified by its key.
	/// </summary>
	/// <param name="id">The ID of the allocation from which to remove the tag.</param>
	/// <param name="key">The tag key to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot access this allocation;
	/// <c>404 Not Found</c> if the allocation or the tag does not exist.
	/// </returns>
	[HttpDelete("{key}")]
	public async Task<IActionResult> DeleteTag(Guid id, string key)
	{
		var (allocation, forbidden) = await GetAuthorizedAllocationAsync(id);
		if (forbidden)
		{
			return Forbid();
		}

		if (allocation is null)
		{
			return NotFound();
		}

		// Look up the specific tag to confirm it exists on this allocation.
		var tag = await _db.AllocationTags
			.FirstOrDefaultAsync(t => t.AllocationId == id && t.Key == key);

		if (tag is null)
		{
			return NotFound();
		}

		_db.AllocationTags.Remove(tag);
		await _db.SaveChangesAsync();

		return NoContent();
	}
}
