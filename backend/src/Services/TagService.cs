using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Manages freeform key-value tags on IP allocations. Tags allow callers to
/// annotate allocations with arbitrary metadata (e.g. environment, owner, cost
/// centre) and then filter allocations by those tags via the list endpoint.
///
/// Tag keys must be unique per allocation. A full replace (<c>PUT</c>) deletes
/// all existing tags and inserts the supplied map, making the operation idempotent.
///
/// Access rules mirror allocation visibility:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Can tag any allocation in the system.</description></item>
///   <item><term>TenantAdmin / TenantUser</term><description>Can only tag allocations within their own tenancy.</description></item>
/// </list>
///
/// Registered as a scoped service.
/// </summary>
public class TagService
{
	/// <summary>EF Core context for tag and allocation queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="TagService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	public TagService(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>
	/// Returns all tags attached to the specified allocation.
	/// </summary>
	/// <param name="allocationId">ID of the allocation whose tags to list.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A list of <see cref="TagResponse"/> objects.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this allocation.</exception>
	/// <exception cref="NotFoundException">Thrown if the allocation does not exist.</exception>
	public async Task<List<TagResponse>> ListAsync(Guid allocationId, CallerContext caller)
	{
		// Resolve and authorise in one step.
		await GetAuthorizedAllocationAsync(allocationId, caller);

		return await _db.AllocationTags
			.Where(t => t.AllocationId == allocationId)
			.Select(t => new TagResponse(t.Id, t.Key, t.Value))
			.ToListAsync();
	}

	/// <summary>
	/// Fully replaces all tags on the specified allocation with the supplied key-value
	/// map. All existing tags are deleted first, then the new set is inserted. This
	/// makes the operation a safe idempotent PUT.
	/// </summary>
	/// <param name="allocationId">ID of the allocation whose tags to replace.</param>
	/// <param name="tags">The new key-value tag set. Duplicate keys are not permitted.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this allocation.</exception>
	/// <exception cref="NotFoundException">Thrown if the allocation does not exist.</exception>
	public async Task<List<TagResponse>> ReplaceAsync(Guid allocationId, Dictionary<string, string> tags, CallerContext caller)
	{
		await GetAuthorizedAllocationAsync(allocationId, caller);

		// Delete all existing tags in one SQL DELETE statement.
		await _db.AllocationTags.Where(t => t.AllocationId == allocationId).ExecuteDeleteAsync();

		// Insert the new tags. The Dictionary<string, string> input deduplicates keys,
		// and the unique index on (AllocationId, Key) provides a database-level safety net.
		foreach (var (key, value) in tags)
		{
			_db.AllocationTags.Add(new AllocationTag
			{
				Id = Guid.NewGuid(),
				AllocationId = allocationId,
				Key = key,
				Value = value
			});
		}

		await _db.SaveChangesAsync();

		// Return the saved tags so the caller can update its state without a follow-up GET.
		return await _db.AllocationTags
			.Where(t => t.AllocationId == allocationId)
			.Select(t => new TagResponse(t.Id, t.Key, t.Value))
			.ToListAsync();
	}

	/// <summary>
	/// Deletes a single tag from an allocation, identified by its key.
	/// </summary>
	/// <param name="allocationId">ID of the allocation from which to remove the tag.</param>
	/// <param name="key">The tag key to delete.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access this allocation.</exception>
	/// <exception cref="NotFoundException">Thrown if the allocation or tag does not exist.</exception>
	public async Task DeleteTagAsync(Guid allocationId, string key, CallerContext caller)
	{
		await GetAuthorizedAllocationAsync(allocationId, caller);

		// Look up the specific tag to confirm it exists on this allocation.
		var tag = await _db.AllocationTags
			.FirstOrDefaultAsync(t => t.AllocationId == allocationId && t.Key == key)
			?? throw new NotFoundException();

		_db.AllocationTags.Remove(tag);
		await _db.SaveChangesAsync();
	}

	// ── Helper ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Resolves the allocation and checks whether the caller is permitted to
	/// operate on it. GlobalAdmin can access any allocation; all other roles are
	/// restricted to allocations within their own tenancy.
	/// </summary>
	/// <param name="allocationId">The allocation ID to look up.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The resolved <see cref="Allocation"/> entity.</returns>
	/// <exception cref="NotFoundException">Thrown if the allocation does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access the allocation.</exception>
	private async Task<Allocation> GetAuthorizedAllocationAsync(Guid allocationId, CallerContext caller)
	{
		var allocation = await _db.Allocations.FindAsync(allocationId)
			?? throw new NotFoundException();

		// GlobalAdmin has unrestricted access to all allocations.
		if (caller.IsGlobalAdmin)
		{
			return allocation;
		}

		// Tenant roles may only access allocations on subnets they can access.
		var subnet = await _db.Subnets.FindAsync(allocation.SubnetId)
			?? throw new NotFoundException("Subnet not found");

		var hasAccess =
			(subnet.Type == SubnetType.Private && subnet.TenancyId == caller.TenancyId) ||
			(subnet.Type == SubnetType.Shared && !await _db.SubnetTenancyAccesses.AnyAsync(sta => sta.SubnetId == subnet.Id)) ||
			(subnet.Type == SubnetType.Shared && await _db.SubnetTenancyAccesses.AnyAsync(sta => sta.SubnetId == subnet.Id && sta.TenancyId == caller.TenancyId));

		if (!hasAccess)
		{
			throw new ForbiddenException();
		}

		return allocation;
	}
}
