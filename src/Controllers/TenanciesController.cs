using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpamService.Controllers;

/// <summary>
/// Manages tenancy lifecycle operations. All endpoints are restricted to the
/// <c>GlobalAdmin</c> role because tenancies define the top-level isolation
/// boundary of the system — only a system administrator should be able to
/// create or destroy one.
///
/// Creating a tenancy also bootstraps an initial <c>TenantAdmin</c> user in
/// the same request so the tenancy is immediately usable. All business logic
/// and data access is delegated to <see cref="TenancyService"/>.
/// </summary>
[ApiController]
[Route("api/tenancies")]
[Authorize(Roles = Roles.GlobalAdmin)]
public class TenanciesController : IpamControllerBase
{
	/// <summary>Service that owns all tenancy business logic and data access.</summary>
	private readonly TenancyService _tenancies;

	/// <summary>
	/// Initialises a new instance of <see cref="TenanciesController"/>.
	/// </summary>
	/// <param name="tenancies">Tenancy service, injected by the DI container.</param>
	public TenanciesController(TenancyService tenancies)
	{
		_tenancies = tenancies;
	}

	/// <summary>
	/// Returns the list of all tenancies registered in the system.
	/// </summary>
	/// <returns><c>200 OK</c> with a list of <see cref="TenancyResponse"/> objects.</returns>
	[HttpGet]
	public async Task<IActionResult> List() =>
		Ok(await _tenancies.ListAsync());

	/// <summary>
	/// Creates a new tenancy and its initial <c>TenantAdmin</c> user in a single
	/// atomic operation. The tenancy name must be unique across the system.
	/// </summary>
	/// <param name="req">Request body with the tenancy details and initial admin credentials.</param>
	/// <returns>
	/// <c>201 Created</c> with the new tenancy on success;
	/// <c>409 Conflict</c> if a tenancy with the same name already exists;
	/// <c>400 Bad Request</c> if Identity rejects the admin credentials.
	/// </returns>
	[HttpPost]
	public Task<IActionResult> Create([FromBody] CreateTenancyRequest req) =>
		ExecuteAsync(async () =>
		{
			// Pass the caller's user ID for audit logging; role check is at the route level.
			var result = await _tenancies.CreateAsync(req, GetCaller().UserId);
			return CreatedAtAction(nameof(List), result);
		});

	/// <summary>
	/// Updates an existing tenancy's name and description.
	/// </summary>
	/// <param name="id">The ID of the tenancy to update.</param>
	/// <param name="req">Request body with the new name and description.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated tenancy on success;
	/// <c>404 Not Found</c> if the tenancy does not exist;
	/// <c>409 Conflict</c> if the new name is already taken.
	/// </returns>
	[HttpPut("{id:guid}")]
	public Task<IActionResult> Update(Guid id, [FromBody] UpdateTenancyRequest req) =>
		ExecuteAsync(async () => Ok(await _tenancies.UpdateAsync(id, req)));

	/// <summary>
	/// Deletes a tenancy and all associated data. This operation is irreversible.
	/// </summary>
	/// <param name="id">The ID of the tenancy to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>404 Not Found</c> if no tenancy with the given ID exists.
	/// </returns>
	[HttpDelete("{id:guid}")]
	public Task<IActionResult> Delete(Guid id) =>
		ExecuteAsync(async () =>
		{
			await _tenancies.DeleteAsync(id, GetCaller().UserId);
			return NoContent();
		});
}
