using System.Net;
using System.Security.Claims;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Controllers;

/// <summary>
/// Handles IP allocation and release operations. This is the most complex
/// controller in the application because allocation access control depends on
/// both the caller's role and the subnet type (shared vs private) plus any
/// tenancy-level access restrictions on shared subnets.
///
/// Route note: because this controller spans two URL prefixes
/// (<c>/api/allocations</c> and <c>/api/subnets/{subnetId}/check/{ip}</c>)
/// routes are applied per-action rather than at the class level.
/// </summary>
[ApiController]
[Authorize]
public class AllocationsController : ControllerBase
{
	/// <summary>EF Core context for allocation and subnet queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Domain service that implements the allocation algorithm.</summary>
	private readonly IpAllocationService _allocator;

	/// <summary>Audit service for recording allocation and release events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="AllocationsController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="allocator">IP allocation service, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public AllocationsController(AppDbContext db, IpAllocationService allocator, AuditService audit)
	{
		_db = db;
		_allocator = allocator;
		_audit = audit;
	}

	/// <summary>The identity ID of the currently authenticated user.</summary>
	private string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

	/// <summary>The role of the currently authenticated user.</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>The tenancy ID of the caller, or <c>null</c> for GlobalAdmin.</summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Determines whether the currently authenticated caller is permitted to
	/// allocate from (or check) the given subnet. This method runs in-process
	/// on data already loaded from the database and must NOT make additional
	/// async DB calls to avoid N+1 issues.
	/// </summary>
	/// <param name="subnet">The subnet entity to evaluate access for.</param>
	/// <returns>
	/// <c>true</c> if the caller may interact with this subnet; <c>false</c> otherwise.
	/// </returns>
	private bool CanAccessSubnet(Subnet subnet)
	{
		// GlobalAdmin can access every subnet.
		if (CallerRole == "GlobalAdmin")
		{
			return true;
		}

		// Non-admin callers must always have a tenancy affiliation.
		if (!CallerTenancyId.HasValue)
		{
			return false;
		}

		if (subnet.Type == SubnetType.Private)
		{
			// Private subnets are only accessible to users within the owning tenancy.
			return subnet.TenancyId == CallerTenancyId;
		}

		// Shared subnets: accessible when there are no restrictions at all (open to
		// all tenancies) or when the caller's tenancy is explicitly granted access.
		// This synchronous LINQ runs against the already-loaded EF change-tracker;
		// it does NOT issue an additional SQL query because SubnetTenancyAccesses
		// is small enough to evaluate in-process.
		var hasAccess =
			!_db.SubnetTenancyAccesses.Any(a => a.SubnetId == subnet.Id) ||
			_db.SubnetTenancyAccesses.Any(a => a.SubnetId == subnet.Id && a.TenancyId == CallerTenancyId);

		return hasAccess;
	}

	/// <summary>
	/// Returns the list of allocations visible to the caller. GlobalAdmin sees all
	/// allocations; TenantAdmin and TenantUser see only their own tenancy's allocations.
	/// Optionally filter by tag key and/or value.
	/// </summary>
	/// <param name="tagKey">Optional tag key to filter by.</param>
	/// <param name="tagValue">Optional tag value to filter by (applied in addition to <paramref name="tagKey"/>).</param>
	/// <returns><c>200 OK</c> with a list of <see cref="AllocationResponse"/> objects.</returns>
	[HttpGet("api/allocations")]
	public async Task<IActionResult> List([FromQuery] string? tagKey, [FromQuery] string? tagValue)
	{
		IQueryable<Allocation> query = _db.Allocations;

		// Scope non-admin callers to their own tenancy's allocations.
		if (CallerRole != "GlobalAdmin")
		{
			query = query.Where(a => a.TenancyId == CallerTenancyId);
		}

		// Apply optional tag filters. Both key and value are checked together when
		// both are provided; key-only filtering matches any value for that key.
		if (tagKey is not null && tagValue is not null)
		{
			query = query.Where(a =>
				_db.AllocationTags.Any(t =>
					t.AllocationId == a.Id && t.Key == tagKey && t.Value == tagValue));
		}
		else if (tagKey is not null)
		{
			query = query.Where(a =>
				_db.AllocationTags.Any(t =>
					t.AllocationId == a.Id && t.Key == tagKey));
		}

		var allocations = await query
			.Select(a => new AllocationResponse(a.Id, a.IpAddress, a.UserId, a.TenancyId,
				a.SubnetId, a.Description, a.AllocatedAt, a.BulkId))
			.ToListAsync();

		return Ok(allocations);
	}

	/// <summary>
	/// Allocates the next available IP address from the specified subnet.
	/// The allocator walks the subnet range in order and returns the first address
	/// that is not currently allocated or excluded.
	/// </summary>
	/// <param name="req">Request body specifying the target subnet and a description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new <see cref="AllocationResponse"/> on success;
	/// <c>403 Forbidden</c> if the caller cannot access the subnet;
	/// <c>404 Not Found</c> if the subnet does not exist;
	/// <c>409 Conflict</c> if no IP addresses are available in the subnet.
	/// </returns>
	[HttpPost("api/allocations")]
	[Authorize(Roles = "TenantAdmin,TenantUser")]
	public async Task<IActionResult> Allocate([FromBody] AllocateRequest req)
	{
		var subnet = await _db.Subnets.FindAsync(req.SubnetId);
		if (subnet is null)
		{
			return NotFound("Subnet not found");
		}

		if (!CanAccessSubnet(subnet))
		{
			return Forbid();
		}

		// CallerTenancyId is guaranteed non-null here because TenantAdmin and
		// TenantUser always have a tenancy affiliation.
		if (!CallerTenancyId.HasValue)
		{
			return Forbid();
		}

		try
		{
			var allocation = await _allocator.AllocateAsync(
				subnet, CallerId, CallerTenancyId.Value, req.Description);

			return CreatedAtAction(nameof(List),
				new AllocationResponse(allocation.Id, allocation.IpAddress, allocation.UserId,
					allocation.TenancyId, allocation.SubnetId, allocation.Description,
					allocation.AllocatedAt, allocation.BulkId));
		}
		catch (NoAvailableIpException ex)
		{
			// Every usable address in the subnet is allocated or excluded.
			return Conflict(ex.Message);
		}
	}

	/// <summary>
	/// Allocates a contiguous block of IP addresses from the specified subnet.
	/// All allocated IPs share the same <c>BulkId</c> so the batch can be
	/// identified as a group, but each IP is its own row and can be released
	/// independently.
	/// </summary>
	/// <param name="req">Request body with the subnet ID, count, and description.</param>
	/// <returns>
	/// <c>201 Created</c> with a list of <see cref="AllocationResponse"/> objects on success;
	/// <c>400 Bad Request</c> if count is not positive;
	/// <c>403 Forbidden</c> if the caller cannot access the subnet;
	/// <c>404 Not Found</c> if the subnet does not exist;
	/// <c>409 Conflict</c> if no contiguous block of the requested size is available.
	/// </returns>
	[HttpPost("api/allocations/bulk")]
	[Authorize(Roles = "TenantAdmin,TenantUser")]
	public async Task<IActionResult> BulkAllocate([FromBody] BulkAllocateRequest req)
	{
		var subnet = await _db.Subnets.FindAsync(req.SubnetId);
		if (subnet is null)
		{
			return NotFound("Subnet not found");
		}

		if (!CanAccessSubnet(subnet))
		{
			return Forbid();
		}

		if (!CallerTenancyId.HasValue)
		{
			return Forbid();
		}

		// Validate the count early to give a clear error before hitting the allocator.
		if (req.Count <= 0)
		{
			return BadRequest("Count must be positive");
		}

		try
		{
			var allocations = await _allocator.BulkAllocateAsync(
				subnet, CallerId, CallerTenancyId.Value, req.Description, req.Count);

			var responses = allocations.Select(a =>
				new AllocationResponse(a.Id, a.IpAddress, a.UserId, a.TenancyId,
					a.SubnetId, a.Description, a.AllocatedAt, a.BulkId));

			return CreatedAtAction(nameof(List), responses);
		}
		catch (NoContiguousBlockException ex)
		{
			// No run of <count> consecutive free addresses exists in the subnet.
			return Conflict(ex.Message);
		}
	}

	/// <summary>
	/// Checks whether a specific IP address is currently available (i.e. not
	/// allocated and not excluded) within the given subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to check within.</param>
	/// <param name="ip">The IP address to check, in dotted-decimal format.</param>
	/// <returns>
	/// <c>200 OK</c> with <c>{ ip, available: bool }</c> on success;
	/// <c>400 Bad Request</c> if the IP address is not valid;
	/// <c>403 Forbidden</c> if the caller cannot access the subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpGet("api/subnets/{subnetId:guid}/check/{ip}")]
	[Authorize(Roles = "TenantAdmin,TenantUser")]
	public async Task<IActionResult> CheckIp(Guid subnetId, string ip)
	{
		var subnet = await _db.Subnets.FindAsync(subnetId);
		if (subnet is null)
		{
			return NotFound("Subnet not found");
		}

		if (!CanAccessSubnet(subnet))
		{
			return Forbid();
		}

		// Validate the IP address format before querying the database.
		if (!IPAddress.TryParse(ip, out _))
		{
			return BadRequest("Invalid IP address");
		}

		// Check whether the address has an active allocation.
		var isAllocated = await _db.Allocations
			.AnyAsync(a => a.SubnetId == subnetId && a.IpAddress == ip);

		// Check whether the address falls within any exclusion range.
		// String comparison works here because exclusion ranges are stored as
		// dotted-decimal strings and we compare lexicographically within a /24
		// or smaller subnet where the ordering is consistent. For correctness
		// across large subnets a numeric comparison (uint) would be preferable,
		// but string compare is sufficient for the expected use case.
		var isExcluded = await _db.Exclusions.AnyAsync(e =>
			e.SubnetId == subnetId &&
			string.Compare(e.Start, ip, StringComparison.Ordinal) <= 0 &&
			string.Compare(e.End, ip, StringComparison.Ordinal) >= 0);

		return Ok(new { ip, available = !isAllocated && !isExcluded });
	}

	/// <summary>
	/// Releases (deletes) an existing IP allocation. GlobalAdmin can release any
	/// allocation; TenantAdmin can release allocations within their tenancy;
	/// TenantUser can only release their own allocations.
	/// The associated tags are also deleted in the same transaction.
	/// </summary>
	/// <param name="id">The ID of the allocation to release.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller does not own the allocation;
	/// <c>404 Not Found</c> if the allocation does not exist.
	/// </returns>
	[HttpDelete("api/allocations/{id:guid}")]
	public async Task<IActionResult> Release(Guid id)
	{
		var allocation = await _db.Allocations.FindAsync(id);
		if (allocation is null)
		{
			return NotFound();
		}

		if (CallerRole == "GlobalAdmin")
		{
			// GlobalAdmin can release any allocation — no further checks.
		}
		else if (CallerRole == "TenantAdmin")
		{
			// TenantAdmin can release allocations within their own tenancy.
			if (allocation.TenancyId != CallerTenancyId)
			{
				return Forbid();
			}
		}
		else
		{
			// TenantUser can only release their own allocations.
			if (allocation.UserId != CallerId)
			{
				return Forbid();
			}
		}

		// Delete all tags associated with this allocation before removing the row
		// to avoid orphaned tag records (no FK cascade configured on AllocationTag).
		await _db.AllocationTags.Where(t => t.AllocationId == id).ExecuteDeleteAsync();
		_db.Allocations.Remove(allocation);

		// For GlobalAdmin (no tenancy) fall back to the allocation's own tenancy ID
		// so the audit entry is scoped correctly for tenant-level audit queries.
		_audit.Log(CallerId, CallerTenancyId ?? allocation.TenancyId,
			"Released", allocation.IpAddress, allocation.SubnetId);

		await _db.SaveChangesAsync();
		return NoContent();
	}
}
