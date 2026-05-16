using System.Net;
using IpamService.Data;
using IpamService.Models;
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
	/// Initialises a new instance of <see cref="IpAllocationService"/>.
	/// </summary>
	/// <param name="db">The EF Core context, injected by the DI container.</param>
	/// <param name="audit">The audit service, injected by the DI container.</param>
	public IpAllocationService(AppDbContext db, AuditService audit)
	{
		_db = db;
		_audit = audit;
	}

	/// <summary>
	/// Finds the first available IP in the subnet, writes an <see cref="Allocation"/>
	/// row and an audit entry, then calls <c>SaveChangesAsync</c> to commit both
	/// atomically. Network and broadcast addresses are never returned.
	/// </summary>
	/// <param name="subnet">The subnet to allocate from.</param>
	/// <param name="userId">Identity ID of the requesting user.</param>
	/// <param name="tenancyId">Tenancy context for the allocation.</param>
	/// <param name="description">Description to store with the allocation.</param>
	/// <returns>The newly created <see cref="Allocation"/> record.</returns>
	/// <exception cref="NoAvailableIpException">Thrown when no usable IP exists in the subnet.</exception>
	public async Task<Allocation> AllocateAsync(
		Subnet subnet,
		string userId,
		Guid tenancyId,
		string description)
	{
		// Build the set of IPs that must not be returned — includes exclusion
		// ranges and all currently-allocated IPs.
		var excludedSet = await BuildExcludedSetAsync(subnet);

		var network = IPNetwork.Parse(subnet.Cidr);

		// Walk the subnet looking for the first non-excluded usable address.
		var ip = FindFirstAvailable(network, excludedSet)
			?? throw new NoAvailableIpException($"No available IP addresses in subnet {subnet.Cidr}");

		// Build the allocation record.
		var allocation = new Allocation
		{
			Id = Guid.NewGuid(),
			IpAddress = ip.ToString(),
			UserId = userId,
			TenancyId = tenancyId,
			SubnetId = subnet.Id,
			Description = description,
			AllocatedAt = DateTime.UtcNow
			// BulkId is null for single allocations — left as default.
		};

		// Stage both the allocation and the audit entry; SaveChangesAsync below
		// commits them in one transaction so neither can exist without the other.
		_db.Allocations.Add(allocation);
		_audit.Log(userId, tenancyId, "Allocated", ip.ToString(), subnet.Id);

		await _db.SaveChangesAsync();
		return allocation;
	}

	/// <summary>
	/// Finds a contiguous block of <paramref name="count"/> available IPs in the
	/// subnet, writes an <see cref="Allocation"/> row per IP (all sharing the same
	/// <c>BulkId</c>) plus an audit entry per IP, then commits everything atomically.
	/// </summary>
	/// <param name="subnet">The subnet to allocate from.</param>
	/// <param name="userId">Identity ID of the requesting user.</param>
	/// <param name="tenancyId">Tenancy context for the allocations.</param>
	/// <param name="description">Description applied to every allocation in the bulk request.</param>
	/// <param name="count">Number of consecutive IPs required. Must be positive.</param>
	/// <returns>The list of newly created <see cref="Allocation"/> records, in ascending IP order.</returns>
	/// <exception cref="NoContiguousBlockException">Thrown when no contiguous block of the requested size exists.</exception>
	public async Task<List<Allocation>> BulkAllocateAsync(
		Subnet subnet,
		string userId,
		Guid tenancyId,
		string description,
		int count)
	{
		// Same exclusion logic as single allocation — both exclusion ranges and
		// already-allocated IPs are treated as unavailable.
		var excludedSet = await BuildExcludedSetAsync(subnet);
		var network = IPNetwork.Parse(subnet.Cidr);

		// Find a run of <count> consecutive addresses with no excluded IPs in between.
		var block = FindContiguousBlock(network, excludedSet, count)
			?? throw new NoContiguousBlockException(
				$"No contiguous block of {count} IPs available in subnet {subnet.Cidr}");

		// All IPs in this bulk request share a single BulkId so callers can
		// identify the batch, while each IP is still its own row (individually releasable).
		var bulkId = Guid.NewGuid();
		var allocations = new List<Allocation>(count);

		foreach (var ip in block)
		{
			var allocation = new Allocation
			{
				Id = Guid.NewGuid(),
				IpAddress = ip.ToString(),
				UserId = userId,
				TenancyId = tenancyId,
				SubnetId = subnet.Id,
				Description = description,
				AllocatedAt = DateTime.UtcNow,
				BulkId = bulkId
			};

			allocations.Add(allocation);
			_db.Allocations.Add(allocation);

			// One audit entry per allocated IP so the audit trail shows every
			// individual address that was handed out.
			_audit.Log(userId, tenancyId, "BulkAllocated", ip.ToString(), subnet.Id, $"BulkId={bulkId}");
		}

		// Commit all allocations and audit entries in one transaction.
		await _db.SaveChangesAsync();
		return allocations;
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
