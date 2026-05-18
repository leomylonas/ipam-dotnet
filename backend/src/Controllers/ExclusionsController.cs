using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Manages IP exclusion ranges within a subnet. Exclusions mark ranges of
/// addresses that the allocator must never assign — useful for reserving gateway
/// addresses, DHCP pools, or other infrastructure IPs.
///
/// Access control (enforced by <see cref="ExclusionService"/>):
/// <list type="bullet">
///   <item><term>Read (GET)</term><description>GlobalAdmin for any subnet; TenantAdmin for any subnet they can see.</description></item>
///   <item><term>Write (POST / PUT / DELETE)</term><description>GlobalAdmin for shared subnets; TenantAdmin for their own private subnets only.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/subnets/{subnetId:guid}/exclusions")]
[Authorize]
public class ExclusionsController : IpamControllerBase
{
	/// <summary>Service that owns all exclusion business logic and access control.</summary>
	private readonly ExclusionService _exclusions;

	/// <summary>
	/// Initialises a new instance of <see cref="ExclusionsController"/>.
	/// </summary>
	/// <param name="exclusions">Exclusion service, injected by the DI container.</param>
	public ExclusionsController(ExclusionService exclusions)
	{
		_exclusions = exclusions;
	}

	/// <summary>
	/// Returns all exclusion ranges defined on the specified subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet whose exclusions to list.</param>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="ExclusionResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller cannot read this subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpGet]
	public Task<IActionResult> List(Guid subnetId) =>
		ExecuteAsync(async () => Ok(await _exclusions.ListAsync(subnetId, GetCaller())));

	/// <summary>
	/// Adds a new IP exclusion range to the specified subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to add the exclusion to.</param>
	/// <param name="req">Request body with start/end IP addresses and a description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new exclusion;
	/// <c>400 Bad Request</c> if either IP address is invalid;
	/// <c>403 Forbidden</c> if the caller cannot write to this subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpPost]
	public Task<IActionResult> Create(Guid subnetId, [FromBody] CreateExclusionRequest req) =>
		ExecuteAsync(async () =>
		{
			var result = await _exclusions.CreateAsync(subnetId, req, GetCaller());
			return CreatedAtAction(nameof(List), new { subnetId }, result);
		});

	/// <summary>
	/// Updates the description of an existing exclusion. Range bounds are immutable.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet that owns the exclusion.</param>
	/// <param name="id">The ID of the exclusion to update.</param>
	/// <param name="req">Request body with the new description.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated exclusion;
	/// <c>403 Forbidden</c> if the caller cannot write to this subnet;
	/// <c>404 Not Found</c> if the subnet or exclusion does not exist.
	/// </returns>
	[HttpPut("{id:guid}")]
	public Task<IActionResult> Update(Guid subnetId, Guid id, [FromBody] UpdateExclusionRequest req) =>
		ExecuteAsync(async () => Ok(await _exclusions.UpdateAsync(subnetId, id, req, GetCaller())));

	/// <summary>
	/// Removes a single exclusion range from a subnet.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet that owns the exclusion.</param>
	/// <param name="id">The ID of the exclusion to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot write to this subnet;
	/// <c>404 Not Found</c> if the subnet or exclusion does not exist.
	/// </returns>
	[HttpDelete("{id:guid}")]
	public Task<IActionResult> Delete(Guid subnetId, Guid id) =>
		ExecuteAsync(async () =>
		{
			await _exclusions.DeleteAsync(subnetId, id, GetCaller());
			return NoContent();
		});
}
