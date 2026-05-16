namespace IpamService.Models;

/// <summary>
/// Defines a range of IP addresses within a subnet that must never be allocated.
/// Common uses include gateway IPs, broadcast addresses, or pre-assigned static
/// devices that are not tracked in this IPAM system.
/// For a single excluded IP, set <see cref="Start"/> and <see cref="End"/> to the same value.
/// </summary>
public class Exclusion
{
	/// <summary>The unique identifier for this exclusion rule.</summary>
	public Guid Id { get; set; }

	/// <summary>The subnet this exclusion applies to.</summary>
	public Guid SubnetId { get; set; }

	/// <summary>
	/// First IP address in the excluded range (inclusive).
	/// Stored as a string to avoid provider-specific type differences.
	/// </summary>
	public string Start { get; set; } = string.Empty;

	/// <summary>
	/// Last IP address in the excluded range (inclusive). Equal to
	/// <see cref="Start"/> for single-IP exclusions.
	/// </summary>
	public string End { get; set; } = string.Empty;

	/// <summary>Human-readable reason this range is excluded, e.g. "Gateway IP".</summary>
	public string Description { get; set; } = string.Empty;
}
