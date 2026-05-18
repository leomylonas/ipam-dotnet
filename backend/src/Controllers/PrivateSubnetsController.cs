using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Manages private subnets within a specific tenancy. Private subnets are
/// exclusively owned by one tenancy, must fall within RFC1918 address space,
/// and are not visible or accessible to other tenancies.
///
/// Access rules:
/// <list type="bullet">
///   <item><term>GlobalAdmin</term><description>Can manage private subnets in any tenancy.</description></item>
///   <item><term>TenantAdmin</term><description>Can manage private subnets only within their own tenancy.</description></item>
///   <item><term>TenantUser</term><description>Cannot manage private subnets.</description></item>
/// </list>
///
/// All business logic and tenancy-level access checks are delegated to <see cref="SubnetService"/>.
/// </summary>
[ApiController]
[Route("api/tenancies/{tenancyId:guid}/subnets")]
[Authorize]
public class PrivateSubnetsController : IpamControllerBase
{
	/// <summary>Service that owns all subnet business logic.</summary>
	private readonly SubnetService _subnets;

	/// <summary>
	/// Initialises a new instance of <see cref="PrivateSubnetsController"/>.
	/// </summary>
	/// <param name="subnets">Subnet service, injected by the DI container.</param>
	public PrivateSubnetsController(SubnetService subnets)
	{
		_subnets = subnets;
	}

	/// <summary>
	/// Returns the list of private subnets belonging to the specified tenancy.
	/// </summary>
	/// <param name="tenancyId">The ID of the tenancy whose subnets to list.</param>
	/// <returns>
	/// <c>200 OK</c> with a list of <see cref="SubnetResponse"/> objects;
	/// <c>403 Forbidden</c> if the caller cannot access this tenancy;
	/// <c>404 Not Found</c> if the tenancy does not exist.
	/// </returns>
	[HttpGet]
	public Task<IActionResult> List(Guid tenancyId) =>
		ExecuteAsync(async () => Ok(await _subnets.ListPrivateAsync(tenancyId, GetCaller())));

	/// <summary>
	/// Creates a new private subnet within the specified tenancy.
	/// </summary>
	/// <param name="tenancyId">The ID of the tenancy that will own the subnet.</param>
	/// <param name="req">Request body with the CIDR, name, and description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new subnet on success;
	/// <c>400 Bad Request</c> if the CIDR is invalid or not RFC1918;
	/// <c>403 Forbidden</c> if the caller cannot manage this tenancy;
	/// <c>404 Not Found</c> if the tenancy does not exist;
	/// <c>409 Conflict</c> if the CIDR overlaps an existing subnet in this tenancy.
	/// </returns>
	[HttpPost]
	public Task<IActionResult> Create(Guid tenancyId, [FromBody] CreateSubnetRequest req) =>
		ExecuteAsync(async () =>
		{
			var result = await _subnets.CreatePrivateAsync(tenancyId, req, GetCaller());
			return CreatedAtAction(nameof(List), new { tenancyId }, result);
		});

	/// <summary>
	/// Updates the name and description of a private subnet.
	/// </summary>
	/// <param name="tenancyId">The ID of the owning tenancy.</param>
	/// <param name="subnetId">The ID of the subnet to update.</param>
	/// <param name="req">New name and description values.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated subnet;
	/// <c>403 Forbidden</c> if the caller cannot manage this tenancy;
	/// <c>404 Not Found</c> if the subnet does not exist in this tenancy.
	/// </returns>
	[HttpPut("{subnetId:guid}")]
	public Task<IActionResult> Update(Guid tenancyId, Guid subnetId, [FromBody] UpdateSubnetRequest req) =>
		ExecuteAsync(async () => Ok(await _subnets.UpdatePrivateAsync(tenancyId, subnetId, req, GetCaller())));

	/// <summary>
	/// Deletes a private subnet and all associated exclusions and allocations.
	/// </summary>
	/// <param name="tenancyId">The ID of the owning tenancy.</param>
	/// <param name="subnetId">The ID of the subnet to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot access this tenancy;
	/// <c>404 Not Found</c> if the subnet does not exist in this tenancy.
	/// </returns>
	[HttpDelete("{subnetId:guid}")]
	public Task<IActionResult> Delete(Guid tenancyId, Guid subnetId) =>
		ExecuteAsync(async () =>
		{
			await _subnets.DeletePrivateAsync(tenancyId, subnetId, GetCaller());
			return NoContent();
		});
}
