using System.Net;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Manages IP exclusion ranges on subnets. Exclusions mark address ranges that
/// the allocator must never assign — useful for gateway addresses, DHCP pools,
/// or other infrastructure IPs.
///
/// Access rules enforced by this service:
/// <list type="bullet">
///   <item><term>Read (list)</term><description>GlobalAdmin for any subnet; TenantAdmin for subnets they own or can see.</description></item>
///   <item><term>Write (create / update / delete)</term><description>GlobalAdmin for shared subnets; TenantAdmin for their own private subnets only.</description></item>
/// </list>
///
/// Registered as a scoped service.
/// </summary>
public class ExclusionService
{
	/// <summary>EF Core context for exclusion and subnet queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>Audit service for staging exclusion lifecycle events.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Initialises a new instance of <see cref="ExclusionService"/>.
	/// </summary>
	/// <param name="db">EF Core context, injected by the DI container.</param>
	/// <param name="audit">Audit service, injected by the DI container.</param>
	public ExclusionService(AppDbContext db, AuditService audit)
	{
		_db = db;
		_audit = audit;
	}

	/// <summary>
	/// Returns all exclusion ranges defined on the specified subnet.
	/// </summary>
	/// <param name="subnetId">ID of the subnet whose exclusions to list.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A list of <see cref="ExclusionResponse"/> objects for the subnet.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot read this subnet's exclusions.</exception>
	/// <exception cref="NotFoundException">Thrown if the subnet does not exist.</exception>
	public async Task<List<ExclusionResponse>> ListAsync(Guid subnetId, CallerContext caller)
	{
		// Resolve the subnet and check access in one step; throws if not found or forbidden.
		await GetAuthorizedSubnetAsync(subnetId, caller, requireWrite: false);

		return await _db.Exclusions
			.Where(e => e.SubnetId == subnetId)
			.Select(e => new ExclusionResponse(e.Id, e.SubnetId, e.Start, e.End, e.Description))
			.ToListAsync();
	}

	/// <summary>
	/// Adds a new IP exclusion range to the specified subnet. Single-IP exclusions
	/// are expressed by setting <c>Start</c> and <c>End</c> to the same address.
	/// Both addresses must be valid IPv4.
	/// </summary>
	/// <param name="subnetId">ID of the subnet to add the exclusion to.</param>
	/// <param name="req">Request body with the start/end IP addresses and a description.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The newly created exclusion as an <see cref="ExclusionResponse"/>.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot write to this subnet.</exception>
	/// <exception cref="NotFoundException">Thrown if the subnet does not exist.</exception>
	/// <exception cref="ValidationException">Thrown if either IP address is invalid.</exception>
	public async Task<ExclusionResponse> CreateAsync(Guid subnetId, CreateExclusionRequest req, CallerContext caller)
	{
		// Write access is stricter than read — TenantAdmin may only modify their own private subnets.
		await GetAuthorizedSubnetAsync(subnetId, caller, requireWrite: true);

		// Validate both addresses before persisting. The allocator parses them at runtime;
		// invalid values stored in the database would cause allocator failures later.
		if (!IPAddress.TryParse(req.Start, out _) || !IPAddress.TryParse(req.End, out _))
		{
			throw new ValidationException("Invalid IP address");
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
		_audit.Log(caller.UserId, caller.TenancyId, "ExclusionAdded", subnetId: subnetId,
			notes: $"Start={req.Start},End={req.End}");
		await _db.SaveChangesAsync();

		return new ExclusionResponse(exclusion.Id, exclusion.SubnetId, exclusion.Start,
			exclusion.End, exclusion.Description);
	}

	/// <summary>
	/// Updates the description of an existing exclusion. The IP range bounds are
	/// immutable once created; to change the range, delete and re-create the exclusion.
	/// </summary>
	/// <param name="subnetId">ID of the subnet that owns the exclusion.</param>
	/// <param name="id">ID of the exclusion to update.</param>
	/// <param name="req">Request body containing the new description.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>The updated exclusion as an <see cref="ExclusionResponse"/>.</returns>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot write to this subnet.</exception>
	/// <exception cref="NotFoundException">Thrown if the subnet or the exclusion does not exist.</exception>
	public async Task<ExclusionResponse> UpdateAsync(Guid subnetId, Guid id, UpdateExclusionRequest req, CallerContext caller)
	{
		await GetAuthorizedSubnetAsync(subnetId, caller, requireWrite: true);

		// Look up the exclusion scoped to the subnet so a wrong subnet ID is still a 404.
		var exclusion = await _db.Exclusions.FirstOrDefaultAsync(e => e.Id == id && e.SubnetId == subnetId)
			?? throw new NotFoundException();

		exclusion.Description = req.Description;
		await _db.SaveChangesAsync();

		return new ExclusionResponse(exclusion.Id, exclusion.SubnetId, exclusion.Start,
			exclusion.End, exclusion.Description);
	}

	/// <summary>
	/// Removes a single exclusion range from a subnet.
	/// </summary>
	/// <param name="subnetId">ID of the subnet that owns the exclusion.</param>
	/// <param name="id">ID of the exclusion to delete.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot write to this subnet.</exception>
	/// <exception cref="NotFoundException">Thrown if the subnet or exclusion does not exist.</exception>
	public async Task DeleteAsync(Guid subnetId, Guid id, CallerContext caller)
	{
		await GetAuthorizedSubnetAsync(subnetId, caller, requireWrite: true);

		// Scope the exclusion lookup to the subnet to prevent cross-subnet deletion.
		var exclusion = await _db.Exclusions.FirstOrDefaultAsync(e => e.Id == id && e.SubnetId == subnetId)
			?? throw new NotFoundException();

		_db.Exclusions.Remove(exclusion);
		_audit.Log(caller.UserId, caller.TenancyId, "ExclusionRemoved", subnetId: subnetId,
			notes: $"Start={exclusion.Start},End={exclusion.End}");
		await _db.SaveChangesAsync();
	}

	// ── Helper ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Resolves the subnet and verifies the caller's access. Throws
	/// <see cref="NotFoundException"/> if the subnet does not exist, or
	/// <see cref="ForbiddenException"/> if the caller is not permitted.
	/// </summary>
	/// <param name="subnetId">The subnet to look up.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <param name="requireWrite">
	/// When <c>true</c>, enforces write-access rules: TenantAdmin may only
	/// operate on their own private subnets. When <c>false</c>, TenantAdmin
	/// may read exclusions on any subnet they can see.
	/// </param>
	/// <returns>The resolved <see cref="Subnet"/> entity.</returns>
	private async Task<Subnet> GetAuthorizedSubnetAsync(Guid subnetId, CallerContext caller, bool requireWrite)
	{
		var subnet = await _db.Subnets.FindAsync(subnetId)
			?? throw new NotFoundException();

		// GlobalAdmin can read and write exclusions on any subnet type.
		if (caller.IsGlobalAdmin)
		{
			return subnet;
		}

		if (caller.IsTenantAdmin)
		{
			if (requireWrite)
			{
				// For write operations TenantAdmin is restricted to their own private subnets.
				// They cannot add or remove exclusions on shared subnets or other tenancies' subnets.
				if (subnet.Type == SubnetType.Private && subnet.TenancyId == caller.TenancyId)
				{
					return subnet;
				}

				throw new ForbiddenException();
			}

			// For read operations TenantAdmin cannot access another tenancy's private subnet.
			if (subnet.Type == SubnetType.Private && subnet.TenancyId != caller.TenancyId)
			{
				throw new ForbiddenException();
			}

			return subnet;
		}

		// TenantUser cannot access exclusions at all.
		throw new ForbiddenException();
	}
}
