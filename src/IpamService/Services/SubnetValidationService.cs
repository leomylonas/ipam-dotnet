using System.Net;
using IpamService.Data;
using IpamService.Models;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Services;

/// <summary>
/// Provides CIDR parsing and IP-network validation logic used when creating subnets.
/// All IP arithmetic uses <see cref="IPNetwork"/> from <c>System.Net</c> — no
/// third-party libraries are required.
/// Registered as a scoped service.
/// </summary>
public class SubnetValidationService
{
	/// <summary>
	/// The three private IP address ranges defined by RFC1918.
	/// Private subnets submitted to this IPAM must fall entirely within one of
	/// these ranges to prevent accidentally routing public IPs internally.
	/// </summary>
	private static readonly IPNetwork[] Rfc1918Ranges =
	[
		IPNetwork.Parse("10.0.0.0/8"),
		IPNetwork.Parse("172.16.0.0/12"),
		IPNetwork.Parse("192.168.0.0/16"),
	];

	/// <summary>The EF Core context used for overlap queries.</summary>
	private readonly AppDbContext _db;

	/// <summary>
	/// Initialises a new instance of <see cref="SubnetValidationService"/>.
	/// </summary>
	/// <param name="db">The EF Core context, injected by the DI container.</param>
	public SubnetValidationService(AppDbContext db)
	{
		_db = db;
	}

	/// <summary>
	/// Attempts to parse a CIDR string into an <see cref="IPNetwork"/>.
	/// </summary>
	/// <param name="cidr">The CIDR string to parse, e.g. <c>192.168.1.0/24</c>.</param>
	/// <param name="network">
	/// When this method returns <c>true</c>, contains the parsed network.
	/// When <c>false</c>, contains <c>default</c>.
	/// </param>
	/// <returns><c>true</c> if parsing succeeded; <c>false</c> otherwise.</returns>
	public bool TryParseCidr(string cidr, out IPNetwork? network)
	{
		// IPNetwork.TryParse returns a non-nullable out parameter in .NET 10;
		// we wrap it in a nullable so callers can use a null-check pattern.
		if (IPNetwork.TryParse(cidr, out var net))
		{
			network = net;
			return true;
		}

		network = default;
		return false;
	}

	/// <summary>
	/// Returns <c>true</c> if the supplied network falls entirely within one
	/// of the RFC1918 private ranges (10/8, 172.16/12, 192.168/16).
	/// </summary>
	/// <param name="network">The network to test.</param>
	/// <returns><c>true</c> when the network is private; <c>false</c> for public address space.</returns>
	public bool IsRfc1918(IPNetwork network)
	{
		// A network is considered RFC1918 if its base address falls inside one
		// of the private ranges AND its prefix is at least as specific as that
		// range (so /7 does not pass even though its base address is in 10/8).
		return Rfc1918Ranges.Any(r =>
			r.Contains(network.BaseAddress) &&
			network.PrefixLength >= r.PrefixLength);
	}

	/// <summary>
	/// Checks whether the supplied CIDR overlaps with any existing subnet of the
	/// same type and scope. Returns a human-readable error string if there is an
	/// overlap, or <c>null</c> if the CIDR is safe to use.
	/// </summary>
	/// <param name="cidr">The CIDR being validated.</param>
	/// <param name="type">Whether the new subnet is Shared or Private — used to scope the query.</param>
	/// <param name="tenancyId">For Private subnets, the tenancy scope to check within. Ignored for Shared.</param>
	/// <param name="excludeSubnetId">
	/// When updating an existing subnet, supply its ID to exclude it from the
	/// overlap check. Pass <c>null</c> for new subnets.
	/// </param>
	/// <returns>An error message string if overlap is detected; <c>null</c> if validation passes.</returns>
	public async Task<string?> CheckOverlapAsync(
		string cidr,
		SubnetType type,
		Guid? tenancyId,
		Guid? excludeSubnetId = null)
	{
		// Validate the incoming CIDR before comparing — return an error early
		// rather than letting an invalid CIDR produce confusing overlap results.
		if (!IPNetwork.TryParse(cidr, out var newNetwork))
		{
			return "Invalid CIDR";
		}

		// Build the query scoped to the same type and (for Private) tenancy.
		IQueryable<Subnet> query = _db.Subnets.Where(s => s.Type == type);

		if (type == SubnetType.Private)
		{
			// Private subnets only compete for overlap within their own tenancy —
			// two different tenancies may use the same private range independently.
			query = query.Where(s => s.TenancyId == tenancyId);
		}

		if (excludeSubnetId.HasValue)
		{
			// When checking whether an edit would cause an overlap, exclude the
			// subnet being edited from the comparison set.
			query = query.Where(s => s.Id != excludeSubnetId.Value);
		}

		// Pull only the CIDR strings into memory to avoid DB-side IP parsing.
		var existing = await query.Select(s => s.Cidr).ToListAsync();

		foreach (var existingCidr in existing)
		{
			// Skip any CIDR in the DB that we can't parse — defensive but shouldn't happen.
			if (!IPNetwork.TryParse(existingCidr, out var existingNetwork))
			{
				continue;
			}

			if (NetworksOverlap(newNetwork, existingNetwork))
			{
				return $"Overlaps with existing subnet {existingCidr}";
			}
		}

		// No overlaps found — the CIDR is safe to use.
		return null;
	}

	/// <summary>
	/// Returns <c>true</c> if two networks share at least one IP address.
	/// This is true when either network's base address falls inside the other.
	/// </summary>
	/// <param name="a">First network to compare.</param>
	/// <param name="b">Second network to compare.</param>
	/// <returns><c>true</c> if the networks overlap; <c>false</c> if they are disjoint.</returns>
	private static bool NetworksOverlap(IPNetwork a, IPNetwork b)
	{
		// Two CIDRs overlap if one contains the base address of the other.
		// Checking both directions handles the case where one is a supernet of
		// the other (e.g. 10.0.0.0/8 overlaps 10.1.0.0/16 in both checks).
		return a.Contains(b.BaseAddress) || b.Contains(a.BaseAddress);
	}
}
