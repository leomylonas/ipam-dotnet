using System.Net;
using IpamService.Data;
using IpamService.Models;
using IpamService.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Thrown when a single-IP allocation request cannot be satisfied because every
/// usable address in the target subnet is either allocated or excluded.
/// </summary>
public class NoAvailableIpException : Exception
{
	/// <summary>
	/// Initialises a new instance with a descriptive message that includes
	/// the subnet CIDR so callers can surface it directly in HTTP responses.
	/// </summary>
	/// <param name="message">Human-readable explanation of why no IP is available.</param>
	public NoAvailableIpException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a bulk allocation request cannot be satisfied because no
/// contiguous block of the requested size exists in the subnet.
/// Maps to HTTP 409 Conflict in the allocation controller.
/// </summary>
public class NoContiguousBlockException : Exception
{
	/// <summary>
	/// Initialises a new instance with a descriptive message that includes
	/// both the requested count and the subnet CIDR.
	/// </summary>
	/// <param name="message">Human-readable explanation of why no block is available.</param>
	public NoContiguousBlockException(string message) : base(message) { }
}

/// <summary>
/// Core service responsible for finding and recording IP allocations.
/// Uses pure integer arithmetic on IPv4 addresses (converting to/from
/// <c>uint</c>) to walk the subnet range efficiently without any
/// third-party IP library dependencies.
/// Registered as a scoped service so it shares the EF context with its callers.
/// </summary>
public class IpAllocationService
{
	/// <summary>The EF Core context used to query and persist allocations.</summary>
	private readonly AppDbContext _db;

	/// <summary>Audit service used to record each allocation event.</summary>
	private readonly AuditService _audit;

	/// <summary>
	/// Maximum number of times <see cref="AllocateAsync"/> and
	/// <see cref="BulkAllocateAsync"/> will retry after a concurrent unique-constraint
	/// violation. Ten attempts is well above any realistic burst of simultaneous
	/// requests to the same subnet.
	/// </summary>
	private const int MaxAllocationAttempts = 10;

	/// <summary>
	/// Initialises a new instance of <see cref="IpAllocationService"/>.
	/// </summary>
	/// <param name="db">The EF Core context, injected by the DI container.</param>
	/// <param name="audit">The audit service, injected by the DI container.</param>
	public IpAllocationService(AppDbContext db, AuditService audit)
	{
		_db = db;
		_audit = audit;
	}

	// ── Public helpers ────────────────────────────────────────────────────────

	/// <summary>
	/// Loads a subnet by its ID from the database, throwing a
	/// <see cref="NotFoundException"/> if it does not exist. Used by controllers
	/// to resolve the subnet before performing access checks and allocation.
	/// </summary>
	/// <param name="subnetId">The ID of the subnet to load.</param>
	/// <returns>The resolved <see cref="Subnet"/> entity.</returns>
	/// <exception cref="NotFoundException">Thrown if no subnet with <paramref name="subnetId"/> exists.</exception>
	public async Task<Subnet> LoadSubnetOrThrowAsync(Guid subnetId) =>
		await _db.Subnets.FindAsync(subnetId)
			?? throw new NotFoundException("Subnet not found");

	// ── Public query and lifecycle methods ───────────────────────────────────

	/// <summary>
	/// Returns the list of allocations visible to the caller. GlobalAdmin sees all
	/// allocations; TenantAdmin and TenantUser see only their own tenancy's allocations.
	/// Optionally filtered by tag key and/or value.
	/// </summary>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <param name="tagKey">Optional tag key to filter by; matched case-sensitively.</param>
	/// <param name="tagValue">Optional tag value; applied in addition to <paramref name="tagKey"/> when both are provided.</param>
	/// <returns>A list of <see cref="AllocationResponse"/> objects.</returns>
	public async Task<List<AllocationResponse>> ListAsync(
		CallerContext caller,
		string? tagKey,
		string? tagValue)
	{
		// Start with all allocations; non-admin callers are scoped to subnets they can access.
		IQueryable<Allocation> query = _db.Allocations;

		if (!caller.IsGlobalAdmin)
		{
			// Include allocations on private subnets owned by the caller's tenancy, plus
			// allocations on shared subnets that the caller's tenancy has access to.
			query = query.Where(a =>
				_db.Subnets.Any(s =>
					s.Id == a.SubnetId &&
					(
						(s.Type == SubnetType.Private && s.TenancyId == caller.TenancyId) ||
						(s.Type == SubnetType.Shared && !_db.SubnetTenancyAccesses.Any(sta => sta.SubnetId == s.Id)) ||
						(s.Type == SubnetType.Shared && _db.SubnetTenancyAccesses.Any(sta => sta.SubnetId == s.Id && sta.TenancyId == caller.TenancyId))
					)));
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

		return await query
			.Select(a => new AllocationResponse(a.Id, a.IpAddress, a.UserId,
				a.SubnetId, a.Description, a.AllocatedAt, a.BulkId))
			.ToListAsync();
	}

	/// <summary>
	/// Releases (deletes) an existing allocation. GlobalAdmin can release any
	/// allocation; TenantAdmin can release allocations within their tenancy;
	/// TenantUser can only release their own allocations. The associated tags are
	/// also deleted atomically.
	/// </summary>
	/// <param name="id">ID of the allocation to release.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <exception cref="NotFoundException">Thrown if the allocation does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller does not have permission to release this allocation.</exception>
	public async Task ReleaseAsync(Guid id, CallerContext caller)
	{
		var allocation = await _db.Allocations.FindAsync(id)
			?? throw new NotFoundException();

		if (caller.IsTenantAdmin)
		{
			// TenantAdmin can release any allocation whose subnet is accessible to their tenancy.
			var subnet = await _db.Subnets.FindAsync(allocation.SubnetId)
				?? throw new NotFoundException("Subnet not found");

			if (!await CanAccessSubnetAsync(subnet, caller))
			{
				throw new ForbiddenException();
			}
		}
		else if (caller.IsTenantUser)
		{
			// TenantUser can only release their own allocations.
			if (allocation.UserId != caller.UserId)
			{
				throw new ForbiddenException();
			}
		}

		// GlobalAdmin: no restrictions.

		// Delete all tags associated with this allocation before removing the row.
		await _db.AllocationTags.Where(t => t.AllocationId == id).ExecuteDeleteAsync();
		_db.Allocations.Remove(allocation);

		_audit.Log(caller.UserId, caller.TenancyId,
			"Released", allocation.IpAddress, allocation.SubnetId);

		await _db.SaveChangesAsync();
	}

	/// <summary>
	/// Checks whether a specific IP address is currently available (not allocated
	/// and not excluded) within the given subnet.
	/// </summary>
	/// <param name="subnetId">ID of the subnet to check within.</param>
	/// <param name="ip">The IP address to check, in dotted-decimal format.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns>A <see cref="CheckIpResponse"/> indicating availability.</returns>
	/// <exception cref="NotFoundException">Thrown if the subnet does not exist.</exception>
	/// <exception cref="ForbiddenException">Thrown if the caller cannot access the subnet.</exception>
	/// <exception cref="ValidationException">Thrown if <paramref name="ip"/> is not a valid IP address.</exception>
	public async Task<CheckIpResponse> CheckIpAsync(Guid subnetId, string ip, CallerContext caller)
	{
		var subnet = await _db.Subnets.FindAsync(subnetId)
			?? throw new NotFoundException("Subnet not found");

		// Enforce subnet-level access the same way allocation does.
		if (!await CanAccessSubnetAsync(subnet, caller))
		{
			throw new ForbiddenException();
		}

		if (!IPAddress.TryParse(ip, out _))
		{
			throw new BadValueException("Invalid IP address.");
		}

		// Check whether the address is currently allocated.
		var isAllocated = await _db.Allocations
			.AnyAsync(a => a.SubnetId == subnetId && a.IpAddress == ip);

		// Check whether the address falls within any exclusion range.
		// String comparison is sufficient for dotted-decimal within subnets where
		// the lexicographic and numeric orderings coincide.
		var isExcluded = await _db.Exclusions.AnyAsync(e =>
			e.SubnetId == subnetId &&
			string.Compare(e.Start, ip, StringComparison.Ordinal) <= 0 &&
			string.Compare(e.End, ip, StringComparison.Ordinal) >= 0);

		return new CheckIpResponse(ip, !isAllocated && !isExcluded);
	}

	/// <summary>
	/// Determines whether the authenticated caller is permitted to allocate from
	/// or check the specified subnet. Shared subnet access restrictions are checked
	/// asynchronously against the database.
	/// </summary>
	/// <param name="subnet">The subnet entity to evaluate access for.</param>
	/// <param name="caller">The context of the authenticated caller.</param>
	/// <returns><c>true</c> if the caller may interact with this subnet; <c>false</c> otherwise.</returns>
	public async Task<bool> CanAccessSubnetAsync(Subnet subnet, CallerContext caller)
	{
		// GlobalAdmin can access every subnet without restriction.
		if (caller.IsGlobalAdmin)
		{
			return true;
		}

		// Non-admin callers must always have a tenancy affiliation.
		if (!caller.TenancyId.HasValue)
		{
			return false;
		}

		if (subnet.Type == SubnetType.Private)
		{
			// Private subnets are only accessible to the owning tenancy.
			return subnet.TenancyId == caller.TenancyId;
		}

		// Shared subnets: accessible when there are no access restrictions (open to all)
		// or when the caller's tenancy has an explicit grant.
		var hasRestrictions = await _db.SubnetTenancyAccesses
			.AnyAsync(a => a.SubnetId == subnet.Id);

		if (!hasRestrictions)
		{
			// No restrictions — open to everyone.
			return true;
		}

		return await _db.SubnetTenancyAccesses
			.AnyAsync(a => a.SubnetId == subnet.Id && a.TenancyId == caller.TenancyId);
	}

	// ── Allocation algorithm ──────────────────────────────────────────────────

	/// <summary>
	/// Finds the first available IP in the subnet, writes an <see cref="Allocation"/>
	/// row and an audit entry, then calls <c>SaveChangesAsync</c> to commit both
	/// atomically. Network and broadcast addresses are never returned.
	/// Any authenticated caller (including GlobalAdmin) may invoke this method;
	/// subnet access must be verified by the caller before invoking.
	///
	/// <para>
	/// <b>Concurrency:</b> Two requests arriving simultaneously can both read the same
	/// "first available" IP before either commits (a classic TOCTOU race). The unique
	/// index on <c>(SubnetId, IpAddress)</c> ensures the database rejects the duplicate;
	/// this method catches the resulting <see cref="DbUpdateException"/> and retries up
	/// to <c>MaxAllocationAttempts</c> times, re-reading the allocation state each time,
	/// so the losing request transparently advances to the next free address.
	/// </para>
	/// </summary>
	/// <param name="subnet">The subnet to allocate from.</param>
	/// <param name="caller">The context of the authenticated caller making the allocation.</param>
	/// <param name="description">Description to store with the allocation.</param>
	/// <returns>The newly created <see cref="Allocation"/> record.</returns>
	/// <exception cref="NoAvailableIpException">Thrown when no usable IP exists in the subnet (after all retry attempts).</exception>
	public async Task<Allocation> AllocateAsync(
		Subnet subnet,
		CallerContext caller,
		string description)
	{
		for (var attempt = 0; attempt < MaxAllocationAttempts; attempt++)
		{
			// Re-read the excluded set on every attempt so that IPs committed by
			// concurrent requests since the last iteration are accounted for.
			var excludedSet = await BuildExcludedSetAsync(subnet);
			var network = IPNetwork.Parse(subnet.Cidr);

			// Walk the subnet looking for the first non-excluded usable address.
			// If the subnet is genuinely full, surface that immediately — retrying
			// would not help because no amount of waiting will free an IP.
			var ip = FindFirstAvailable(network, excludedSet)
				?? throw new NoAvailableIpException($"No available IP addresses in subnet {subnet.Cidr}");

			// Build the allocation record.
			var allocation = new Allocation
			{
				Id = Guid.NewGuid(),
				IpAddress = ip.ToString(),
				UserId = caller.UserId,
				SubnetId = subnet.Id,
				Description = description,
				AllocatedAt = DateTime.UtcNow
				// BulkId is null for single allocations — left as default.
			};

			// Stage both the allocation and the audit entry; SaveChangesAsync below
			// commits them in one transaction so neither can exist without the other.
			_db.Allocations.Add(allocation);
			_audit.Log(caller.UserId, caller.TenancyId, "Allocated", ip.ToString(), subnet.Id);

			try
			{
				await _db.SaveChangesAsync();
				return allocation;
			}
			catch (DbUpdateException) when (attempt < MaxAllocationAttempts - 1)
			{
				// The unique index rejected this IP because a concurrent request
				// committed the same address first. Clear the EF change tracker
				// so the failed allocation and staged audit entry do not carry
				// over into the next iteration, then retry with a fresh read.
				_db.ChangeTracker.Clear();
			}
		}

		// All retry attempts were exhausted — the subnet is effectively full under
		// concurrent load (each attempt kept racing to the same last free address).
		throw new NoAvailableIpException($"No available IP addresses in subnet {subnet.Cidr}");
	}

	/// <summary>
	/// Finds a contiguous block of <paramref name="count"/> available IPs in the
	/// subnet, writes an <see cref="Allocation"/> row per IP (all sharing the same
	/// <c>BulkId</c>) plus an audit entry per IP, then commits everything atomically.
	///
	/// <para>
	/// <b>Concurrency:</b> Like <see cref="AllocateAsync"/>, this method re-reads the
	/// allocation state on each retry attempt. If a concurrent commit causes a unique
	/// constraint violation, the entire block is discarded and a new contiguous block
	/// is searched for from the updated state. A new <c>BulkId</c> is generated on
	/// each attempt so the returned allocations are always internally consistent.
	/// </para>
	/// </summary>
	/// <param name="subnet">The subnet to allocate from.</param>
	/// <param name="caller">The context of the authenticated caller making the allocation.</param>
	/// <param name="description">Description applied to every allocation in the bulk request.</param>
	/// <param name="count">Number of consecutive IPs required. Must be positive.</param>
	/// <returns>The list of newly created <see cref="Allocation"/> records, in ascending IP order.</returns>
	/// <exception cref="NoContiguousBlockException">Thrown when no contiguous block of the requested size exists (after all retry attempts).</exception>
	public async Task<List<Allocation>> BulkAllocateAsync(
		Subnet subnet,
		CallerContext caller,
		string description,
		int count)
	{
		for (var attempt = 0; attempt < MaxAllocationAttempts; attempt++)
		{
			// Re-read the excluded set on every attempt so concurrent commits are
			// reflected before we search for a block.
			var excludedSet = await BuildExcludedSetAsync(subnet);
			var network = IPNetwork.Parse(subnet.Cidr);

			// Find a run of <count> consecutive addresses with no excluded IPs in between.
			var block = FindContiguousBlock(network, excludedSet, count)
				?? throw new NoContiguousBlockException(
					$"No contiguous block of {count} IPs available in subnet {subnet.Cidr}");

			// All IPs in this bulk request share a single BulkId so callers can
			// identify the batch, while each IP is still its own row (individually releasable).
			// A new BulkId is generated on each retry so the returned allocations are
			// always internally consistent regardless of which attempt succeeds.
			var bulkId = Guid.NewGuid();
			var allocations = new List<Allocation>(count);

			foreach (var ip in block)
			{
				var allocation = new Allocation
				{
					Id = Guid.NewGuid(),
					IpAddress = ip.ToString(),
					UserId = caller.UserId,
					SubnetId = subnet.Id,
					Description = description,
					AllocatedAt = DateTime.UtcNow,
					BulkId = bulkId
				};

				allocations.Add(allocation);
				_db.Allocations.Add(allocation);

				// One audit entry per allocated IP so the audit trail shows every
				// individual address that was handed out.
				_audit.Log(caller.UserId, caller.TenancyId, "BulkAllocated", ip.ToString(), subnet.Id, $"BulkId={bulkId}");
			}

			try
			{
				// Commit all allocations and audit entries in one transaction.
				await _db.SaveChangesAsync();
				return allocations;
			}
			catch (DbUpdateException) when (attempt < MaxAllocationAttempts - 1)
			{
				// A concurrent request committed one or more of the same IPs first.
				// Clear the change tracker so none of the failed rows carry forward,
				// then retry with a fresh read of the current allocation state.
				_db.ChangeTracker.Clear();
			}
		}

		throw new NoContiguousBlockException(
			$"No contiguous block of {count} IPs available in subnet {subnet.Cidr}");
	}

	/// <summary>
	/// Builds the set of IPv4 addresses (as uint32) that are unavailable for
	/// allocation in the given subnet. This includes every IP in every configured
	/// exclusion range plus every IP that is currently allocated.
	/// </summary>
	/// <param name="subnet">The subnet whose exclusions and allocations should be loaded.</param>
	/// <returns>A set of uint32 IP values that must not be returned by the allocator.</returns>
	private async Task<HashSet<uint>> BuildExcludedSetAsync(Subnet subnet)
	{
		// Load all exclusion rules for this subnet from the database.
		var exclusions = await _db.Exclusions
			.Where(e => e.SubnetId == subnet.Id)
			.ToListAsync();

		// Load all currently allocated IPs as strings, then convert below.
		var allocated = await _db.Allocations
			.Where(a => a.SubnetId == subnet.Id)
			.Select(a => a.IpAddress)
			.ToListAsync();

		var excluded = new HashSet<uint>();

		// Expand each exclusion range into individual uint32 values.
		// Single-IP exclusions have Start == End, so the loop runs once.
		foreach (var excl in exclusions)
		{
			var start = IpToUint(IPAddress.Parse(excl.Start));
			var end = IpToUint(IPAddress.Parse(excl.End));

			for (var i = start; i <= end; i++)
			{
				excluded.Add(i);
			}
		}

		// Add every currently-allocated IP so the allocator never double-assigns.
		foreach (var ip in allocated)
		{
			excluded.Add(IpToUint(IPAddress.Parse(ip)));
		}

		return excluded;
	}

	/// <summary>
	/// Walks the usable host range of a subnet (first IP after network address
	/// through last IP before broadcast) and returns the first IP not present
	/// in <paramref name="excluded"/>, or <c>null</c> if all are taken.
	/// </summary>
	/// <param name="network">The subnet to scan.</param>
	/// <param name="excluded">Set of unavailable uint32 IP values.</param>
	/// <returns>The first available <see cref="IPAddress"/>, or <c>null</c>.</returns>
	private static IPAddress? FindFirstAvailable(IPNetwork network, HashSet<uint> excluded)
	{
		// The usable range starts at base+1 (skip network address) and ends at
		// broadcast-1 (skip broadcast address).
		var start = IpToUint(network.BaseAddress) + 1;
		var end = IpToUint(GetBroadcast(network)) - 1;

		for (var i = start; i <= end; i++)
		{
			if (!excluded.Contains(i))
			{
				return UintToIp(i);
			}
		}

		// Every usable address in the subnet is taken or excluded.
		return null;
	}

	/// <summary>
	/// Scans the usable host range looking for a run of <paramref name="count"/>
	/// consecutive IPs none of which appear in <paramref name="excluded"/>.
	/// Returns the IPs in ascending order, or <c>null</c> if no such block exists.
	/// </summary>
	/// <param name="network">The subnet to scan.</param>
	/// <param name="excluded">Set of unavailable uint32 IP values.</param>
	/// <param name="count">Number of consecutive IPs required.</param>
	/// <returns>An ordered list of the block's IP addresses, or <c>null</c>.</returns>
	private static List<IPAddress>? FindContiguousBlock(
		IPNetwork network,
		HashSet<uint> excluded,
		int count)
	{
		var start = IpToUint(network.BaseAddress) + 1;
		var end = IpToUint(GetBroadcast(network)) - 1;

		// Track the length of the current run of non-excluded IPs and where it started.
		var consecutive = 0;
		var blockStart = start;

		for (var i = start; i <= end; i++)
		{
			if (!excluded.Contains(i))
			{
				// This address is available — extend (or begin) the current run.
				if (consecutive == 0)
				{
					blockStart = i;
				}

				consecutive++;

				if (consecutive == count)
				{
					// Found a run of the required length — collect and return it.
					var block = new List<IPAddress>(count);

					for (var j = blockStart; j < blockStart + count; j++)
					{
						block.Add(UintToIp(j));
					}

					return block;
				}
			}
			else
			{
				// This address is unavailable — reset the run counter.
				consecutive = 0;
			}
		}

		// No contiguous block of the requested size was found.
		return null;
	}

	/// <summary>
	/// Computes the broadcast address of a network by ORing the base address
	/// with a host mask derived from the prefix length.
	/// </summary>
	/// <param name="network">The network whose broadcast address is needed.</param>
	/// <returns>The broadcast <see cref="IPAddress"/> for the network.</returns>
	private static IPAddress GetBroadcast(IPNetwork network)
	{
		var baseUint = IpToUint(network.BaseAddress);

		// The host mask has 1s in every host bit position.
		// For a /24 that is 0x000000FF (8 host bits → 255).
		var hostBits = 32 - network.PrefixLength;
		var broadcastUint = baseUint | ((1u << hostBits) - 1);

		return UintToIp(broadcastUint);
	}

	/// <summary>
	/// Converts a dotted-decimal <see cref="IPAddress"/> to a big-endian
	/// <c>uint</c> for efficient integer arithmetic.
	/// </summary>
	/// <param name="ip">The IPv4 address to convert. Must be IPv4.</param>
	/// <returns>Big-endian uint32 representation of the address.</returns>
	public static uint IpToUint(IPAddress ip)
	{
		var bytes = ip.GetAddressBytes();

		// GetAddressBytes returns bytes in network order (big-endian), so
		// byte[0] is the most significant octet.
		return ((uint)bytes[0] << 24)
			| ((uint)bytes[1] << 16)
			| ((uint)bytes[2] << 8)
			| bytes[3];
	}

	/// <summary>
	/// Converts a big-endian <c>uint</c> back to an <see cref="IPAddress"/>.
	/// </summary>
	/// <param name="value">Big-endian uint32 representation of an IPv4 address.</param>
	/// <returns>The corresponding <see cref="IPAddress"/>.</returns>
	public static IPAddress UintToIp(uint value)
	{
		// Reconstruct the four octets from the uint by shifting and masking.
		return new IPAddress([
			(byte)(value >> 24),
			(byte)(value >> 16),
			(byte)(value >> 8),
			(byte)value
		]);
	}
}
