using System.ComponentModel.DataAnnotations;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Handles IP allocation and release operations. Business logic — including
/// subnet access checks, the allocation algorithm, and release authorization —
/// is fully delegated to <see cref="IpAllocationService"/>.
///
/// Route note: because this controller spans two URL prefixes
/// (<c>/api/allocations</c> and <c>/api/subnets/{subnetId}/check/{ip}</c>)
/// routes are applied per-action rather than at the class level.
/// </summary>
[ApiController]
[Authorize]
public class AllocationsController : IpamControllerBase
{
	/// <summary>Service that owns all allocation business logic.</summary>
	private readonly IpAllocationService _allocator;

	/// <summary>
	/// Initialises a new instance of <see cref="AllocationsController"/>.
	/// </summary>
	/// <param name="allocator">IP allocation service, injected by the DI container.</param>
	public AllocationsController(IpAllocationService allocator)
	{
		_allocator = allocator;
	}

	/// <summary>
	/// Returns the list of allocations visible to the caller. GlobalAdmin sees all
	/// allocations; TenantAdmin and TenantUser see only their own tenancy's allocations.
	/// Optionally filter by tag key and/or value.
	/// </summary>
	/// <param name="tagKey">Optional tag key to filter by.</param>
	/// <param name="tagValue">Optional tag value to filter by (combined with <paramref name="tagKey"/>).</param>
	/// <returns><c>200 OK</c> with a list of <see cref="AllocationResponse"/> objects.</returns>
	[HttpGet("api/allocations")]
	public async Task<IActionResult> List(
		[FromQuery] string? tagKey,
		[FromQuery] string? tagValue) =>
		Ok(await _allocator.ListAsync(GetCaller(), tagKey, tagValue));

	/// <summary>
	/// Allocates the next available IP address from the specified subnet.
	/// </summary>
	/// <param name="req">Request body specifying the target subnet and a description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new <see cref="AllocationResponse"/>;
	/// <c>403 Forbidden</c> if the caller cannot access the subnet;
	/// <c>404 Not Found</c> if the subnet does not exist;
	/// <c>409 Conflict</c> if no IP addresses are available.
	/// </returns>
	[HttpPost("api/allocations")]
	public Task<IActionResult> Allocate([FromBody] AllocateRequest req) =>
		ExecuteAsync(async () =>
		{
			var caller = GetCaller();

			var subnet = await _allocator.LoadSubnetOrThrowAsync(req.SubnetId);

			if (!await _allocator.CanAccessSubnetAsync(subnet, caller))
			{
				throw new ForbiddenException();
			}

			var allocation = await _allocator.AllocateAsync(subnet, caller, req.Description);

			return CreatedAtAction(nameof(List),
				new AllocationResponse(allocation.Id, allocation.IpAddress, allocation.UserId,
					allocation.SubnetId, allocation.Description,
					allocation.AllocatedAt, allocation.BulkId));
		});

	/// <summary>
	/// Allocates a contiguous block of IP addresses from the specified subnet.
	/// All allocated IPs share the same <c>BulkId</c>.
	/// </summary>
	/// <param name="req">Request body with subnet ID, count, and description.</param>
	/// <returns>
	/// <c>201 Created</c> with a list of <see cref="AllocationResponse"/> objects;
	/// <c>400 Bad Request</c> if count is not positive;
	/// <c>403 Forbidden</c> if the caller cannot access the subnet;
	/// <c>404 Not Found</c> if the subnet does not exist;
	/// <c>409 Conflict</c> if no contiguous block of the requested size exists.
	/// </returns>
	[HttpPost("api/allocations/bulk")]
	public Task<IActionResult> BulkAllocate([FromBody] BulkAllocateRequest req) =>
		ExecuteAsync(async () =>
		{
			var caller = GetCaller();

			var subnet = await _allocator.LoadSubnetOrThrowAsync(req.SubnetId);

			if (!await _allocator.CanAccessSubnetAsync(subnet, caller))
			{
				throw new ForbiddenException();
			}

			if (req.Count <= 0)
			{
				throw new ValidationException(new ValidationResult("Count must be positive.", ["count"]), null, req.Count);
			}

			var allocations = await _allocator.BulkAllocateAsync(subnet, caller, req.Description, req.Count);

			var responses = allocations.Select(a =>
				new AllocationResponse(a.Id, a.IpAddress, a.UserId,
					a.SubnetId, a.Description, a.AllocatedAt, a.BulkId));

			return CreatedAtAction(nameof(List), responses);
		});

	/// <summary>
	/// Checks whether a specific IP address is currently available within the given subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to check within.</param>
	/// <param name="ip">The IP address to check, in dotted-decimal format.</param>
	/// <returns>
	/// <c>200 OK</c> with a <see cref="CheckIpResponse"/>;
	/// <c>400 Bad Request</c> if the IP address is not valid;
	/// <c>403 Forbidden</c> if the caller cannot access the subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpGet("api/subnets/{subnetId:guid}/check/{ip}")]
	[Authorize(Roles = Roles.TenantMembers)]
	public Task<IActionResult> CheckIp(Guid subnetId, string ip) =>
		ExecuteAsync(async () => Ok(await _allocator.CheckIpAsync(subnetId, ip, GetCaller())));

	/// <summary>
	/// Releases (deletes) an existing IP allocation. The associated tags are
	/// also deleted in the same transaction.
	/// </summary>
	/// <param name="id">The ID of the allocation to release.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller does not own the allocation;
	/// <c>404 Not Found</c> if the allocation does not exist.
	/// </returns>
	[HttpDelete("api/allocations/{id:guid}")]
	public Task<IActionResult> Release(Guid id) =>
		ExecuteAsync(async () =>
		{
			await _allocator.ReleaseAsync(id, GetCaller());
			return NoContent();
		});
}
