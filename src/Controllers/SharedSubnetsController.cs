using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Manages shared subnets — subnets that are created and owned by GlobalAdmin
/// and may be made accessible to one or more tenancies, or to all tenancies
/// when no explicit access restriction exists.
///
/// Access control summary:
/// <list type="bullet">
///   <item><term>List</term><description>All authenticated users; filtered by tenancy access for non-admins.</description></item>
///   <item><term>Create / Update / Delete / GrantAccess / RevokeAccess</term><description>GlobalAdmin only.</description></item>
/// </list>
///
/// All business logic is delegated to <see cref="SubnetService"/>.
/// </summary>
[ApiController]
[Route("api/subnets/shared")]
[Authorize]
public class SharedSubnetsController : IpamControllerBase
{
	/// <summary>Service that owns all shared subnet business logic.</summary>
	private readonly SubnetService _subnets;

	/// <summary>
	/// Initialises a new instance of <see cref="SharedSubnetsController"/>.
	/// </summary>
	/// <param name="subnets">Subnet service, injected by the DI container.</param>
	public SharedSubnetsController(SubnetService subnets)
	{
		_subnets = subnets;
	}

	/// <summary>
	/// Returns all shared subnets accessible to the caller.
	/// </summary>
	/// <returns><c>200 OK</c> with a list of <see cref="SubnetResponse"/> objects.</returns>
	[HttpGet]
	public async Task<IActionResult> List() =>
		Ok(await _subnets.ListSharedAsync(GetCaller()));

	/// <summary>
	/// Creates a new shared subnet. Only GlobalAdmin may call this endpoint.
	/// </summary>
	/// <param name="req">Request body with CIDR, name, and description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new subnet;
	/// <c>400 Bad Request</c> if the CIDR is invalid;
	/// <c>409 Conflict</c> if the CIDR overlaps an existing shared subnet.
	/// </returns>
	[HttpPost]
	[Authorize(Roles = Roles.GlobalAdmin)]
	public Task<IActionResult> Create([FromBody] CreateSubnetRequest req) =>
		ExecuteAsync(async () =>
		{
			var result = await _subnets.CreateSharedAsync(req, GetCaller().UserId);
			return CreatedAtAction(nameof(List), result);
		});

	/// <summary>
	/// Updates the name and description of a shared subnet. Only GlobalAdmin may call this.
	/// </summary>
	/// <param name="id">ID of the shared subnet to update.</param>
	/// <param name="req">New name and description.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpPut("{id:guid}")]
	[Authorize(Roles = Roles.GlobalAdmin)]
	public Task<IActionResult> Update(Guid id, [FromBody] UpdateSubnetRequest req) =>
		ExecuteAsync(async () => Ok(await _subnets.UpdateSharedAsync(id, req)));

	/// <summary>
	/// Deletes a shared subnet and all associated data. Only GlobalAdmin may call this.
	/// </summary>
	/// <param name="id">ID of the shared subnet to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.GlobalAdmin)]
	public Task<IActionResult> Delete(Guid id) =>
		ExecuteAsync(async () =>
		{
			await _subnets.DeleteSharedAsync(id, GetCaller().UserId);
			return NoContent();
		});

	/// <summary>
	/// Adds a tenancy-level access restriction to a shared subnet.
	/// </summary>
	/// <param name="id">ID of the shared subnet to restrict.</param>
	/// <param name="req">Request body identifying the tenancy to grant access to.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the subnet or tenancy does not exist;
	/// <c>409 Conflict</c> if access for this tenancy is already granted.
	/// </returns>
	[HttpPost("{id:guid}/access")]
	[Authorize(Roles = Roles.GlobalAdmin)]
	public Task<IActionResult> GrantAccess(Guid id, [FromBody] GrantSubnetAccessRequest req) =>
		ExecuteAsync(async () =>
		{
			await _subnets.GrantAccessAsync(id, req.TenancyId);
			return NoContent();
		});

	/// <summary>
	/// Removes a tenancy-level access restriction from a shared subnet.
	/// </summary>
	/// <param name="id">ID of the shared subnet.</param>
	/// <param name="tenancyId">ID of the tenancy whose access to revoke.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if the access grant does not exist.
	/// </returns>
	[HttpDelete("{id:guid}/access/{tenancyId:guid}")]
	[Authorize(Roles = Roles.GlobalAdmin)]
	public Task<IActionResult> RevokeAccess(Guid id, Guid tenancyId) =>
		ExecuteAsync(async () =>
		{
			await _subnets.RevokeAccessAsync(id, tenancyId);
			return NoContent();
		});
}
