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
/// Manages IP exclusion ranges within a subnet. Exclusions mark ranges of
/// addresses that the allocator must never assign — useful for reserving gateway
/// addresses, DHCP pools, or other infrastructure IPs.
///
/// Access control:
/// <list type="bullet">
///   <item><term>Read (GET)</term><description>GlobalAdmin for any subnet; TenantAdmin for any subnet they can see.</description></item>
///   <item><term>Write (POST / DELETE)</term><description>GlobalAdmin for shared subnets; TenantAdmin for their own private subnets only.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/subnets/{subnetId:guid}/exclusions")]
[Authorize]
public class ExclusionsController : ControllerBase
{
	/// <summary>EF Core context for exclusion and subnet queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Audit service for recording exclusion lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="ExclusionsController"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public ExclusionsController(AppDbContext db, AuditService audit)
	{
		_db = db;
		_audit = audit;
	}

	/// <summary>The identity ID of the currently authenticated user.</summary>
	private string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

	/// <summary>The role of the currently authenticated user.</summary>
	private string CallerRole => User.FindFirstValue(ClaimTypes.Role)!;

	/// <summary>The tenancy ID of the caller, or <c>null</c> for GlobalAdmin.</summary>
	private Guid? CallerTenancyId => Guid.TryParse(User.FindFirstValue("TenancyId"), out var g) ? g : null;

	/// <summary>
	/// Resolves whether the caller is authorised to operate on the given subnet,
	/// returning the subnet entity on success or signalling forbidden/not-found.
	/// </summary>
	/// <param name="subnetId">The subnet to look up and authorise.</param>
	/// <param name="requireWrite">
	/// When <c>true</c>, stricter write-access rules are applied: TenantAdmin may
	/// only operate on their own private subnets, not shared subnets.
	/// When <c>false</c>, TenantAdmin may read exclusions on any subnet they can see.
	/// </param>
	/// <returns>
	/// A tuple of the resolved <see cref="Subnet"/> (or <c>null</c> if not found)
	/// and a boolean indicating whether the caller is forbidden from accessing it.
	/// </returns>
	private async Task<(Subnet? subnet, bool forbidden)> GetAuthorizedSubnetAsync(Guid subnetId, bool requireWrite = false)
	{
		var subnet = await _db.Subnets.FindAsync(subnetId);

		// If the subnet does not exist at all, signal not-found (forbidden = false).
		if (subnet is null)
		{
			return (null, false);
		}

		// GlobalAdmin can read and write exclusions on any subnet type.
		if (CallerRole == "GlobalAdmin")
		{
			return (subnet, false);
		}

		if (CallerRole == "TenantAdmin")
		{
			if (requireWrite)
			{
				// For write operations TenantAdmin is restricted to their own private subnets.
				// They cannot add or remove exclusions on shared subnets.
				if (subnet.Type == SubnetType.Private && subnet.TenancyId == CallerTenancyId)
				{
					return (subnet, false);
				}

				// Either a shared subnet or a private subnet belonging to a different tenancy.
				return (null, true);
			}

			// For read operations TenantAdmin can see exclusions on their own private
			// subnets. Accessing another tenancy's private subnet is forbidden.
			if (subnet.Type == SubnetType.Private && subnet.TenancyId != CallerTenancyId)
			{
				return (null, true);
			}

			return (subnet, false);
		}

		// TenantUser cannot access exclusions at all.
		return (null, true);
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
	public async Task<IActionResult> List(Guid subnetId)
	{
		var (subnet, forbidden) = await GetAuthorizedSubnetAsync(subnetId);
		if (forbidden)
		{
			return Forbid();
		}

		if (subnet is null)
		{
			return NotFound();
		}

		var exclusions = await _db.Exclusions
			.Where(e => e.SubnetId == subnetId)
			.Select(e => new ExclusionResponse(e.Id, e.SubnetId, e.Start, e.End, e.Description))
			.ToListAsync();

		return Ok(exclusions);
	}

	/// <summary>
	/// Adds a new IP exclusion range to the specified subnet. Single-IP exclusions
	/// are expressed by setting <c>Start</c> and <c>End</c> to the same address.
	/// Both addresses must be valid IPv4 addresses.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to add the exclusion to.</param>
	/// <param name="req">Request body with the start/end IP addresses and a description.</param>
	/// <returns>
	/// <c>201 Created</c> with the new exclusion on success;
	/// <c>400 Bad Request</c> if either IP address is invalid;
	/// <c>403 Forbidden</c> if the caller cannot write to this subnet;
	/// <c>404 Not Found</c> if the subnet does not exist.
	/// </returns>
	[HttpPost]
	public async Task<IActionResult> Create(Guid subnetId, [FromBody] CreateExclusionRequest req)
	{
		var (subnet, forbidden) = await GetAuthorizedSubnetAsync(subnetId, requireWrite: true);
		if (forbidden)
		{
			return Forbid();
		}

		if (subnet is null)
		{
			return NotFound();
		}

		// Validate both addresses before persisting. We store them as strings
		// but the allocator parses them at allocation time; invalid values would
		// cause runtime errors there.
		if (!IPAddress.TryParse(req.Start, out _) || !IPAddress.TryParse(req.End, out _))
		{
			return BadRequest("Invalid IP address");
		}

		var exclusion = new Exclusion
		{
			Id = Guid.NewGuid(),
			SubnetId = subnetId,
			Start = req.Start,
			End = req.End,
			Description = req.Description
		};
		_db.Exclusions.Add(exclusion);
		_audit.Log(CallerId, CallerTenancyId, "ExclusionAdded", subnetId: subnetId,
			notes: $"Start={req.Start},End={req.End}");
		await _db.SaveChangesAsync();

		return CreatedAtAction(nameof(List), new { subnetId },
			new ExclusionResponse(exclusion.Id, exclusion.SubnetId, exclusion.Start,
				exclusion.End, exclusion.Description));
	}

	/// <summary>
	/// Updates an exclusion description. Range bounds are immutable.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet that owns the exclusion.</param>
	/// <param name="id">The ID of the exclusion to update.</param>
	/// <param name="req">Request body with updated description.</param>
	/// <returns>
	/// <c>200 OK</c> with the updated exclusion on success;
	/// <c>403 Forbidden</c> if the caller cannot write to this subnet;
	/// <c>404 Not Found</c> if the subnet or exclusion does not exist.
	/// </returns>
	[HttpPut("{id:guid}")]
	public async Task<IActionResult> Update(Guid subnetId, Guid id, [FromBody] UpdateExclusionRequest req)
	{
		var (subnet, forbidden) = await GetAuthorizedSubnetAsync(subnetId, requireWrite: true);
		if (forbidden)
		{
			return Forbid();
		}

		if (subnet is null)
		{
			return NotFound();
		}

		var exclusion = await _db.Exclusions.FirstOrDefaultAsync(e => e.Id == id && e.SubnetId == subnetId);
		if (exclusion is null)
		{
			return NotFound();
		}

		exclusion.Description = req.Description;
		await _db.SaveChangesAsync();

		return Ok(new ExclusionResponse(exclusion.Id, exclusion.SubnetId, exclusion.Start,
			exclusion.End, exclusion.Description));
	}

	/// <summary>
	/// Removes a single exclusion range from a subnet by its ID.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet that owns the exclusion.</param>
	/// <param name="id">The ID of the exclusion to delete.</param>
	/// <returns>
	/// <c>204 No Content</c> on success;
	/// <c>403 Forbidden</c> if the caller cannot write to this subnet;
	/// <c>404 Not Found</c> if the subnet or the exclusion does not exist.
	/// </returns>
	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> Delete(Guid subnetId, Guid id)
	{
		var (subnet, forbidden) = await GetAuthorizedSubnetAsync(subnetId, requireWrite: true);
		if (forbidden)
		{
			return Forbid();
		}

		if (subnet is null)
		{
			return NotFound();
		}

		// Look up the exclusion scoped to the subnet so that an ID from a different
		// subnet cannot accidentally be deleted.
		var exclusion = await _db.Exclusions.FirstOrDefaultAsync(e => e.Id == id && e.SubnetId == subnetId);
		if (exclusion is null)
		{
			return NotFound();
		}

		_db.Exclusions.Remove(exclusion);
		_audit.Log(CallerId, CallerTenancyId, "ExclusionRemoved", subnetId: subnetId,
			notes: $"Start={exclusion.Start},End={exclusion.End}");
		await _db.SaveChangesAsync();

		return NoContent();
	}
}
