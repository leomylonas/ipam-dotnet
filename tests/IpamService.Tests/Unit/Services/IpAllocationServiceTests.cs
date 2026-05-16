using System.Net;
using IpamService.Services;

namespace IpamService.Tests.Unit.Services;

/// <summary>
/// Unit tests for the IP arithmetic helpers on <see cref="IpAllocationService"/>.
/// These helpers are <c>public static</c> so they can be exercised independently
/// of the EF context and database that the full service requires.
///
/// The allocator's higher-level methods (AllocateAsync, BulkAllocateAsync) are
/// covered by the integration tests in <c>AllocationsControllerTests</c> because
/// they require a running EF context and real database to be meaningful.
/// </summary>
public class IpAllocationServiceTests
{
	/// <summary>
	/// Verifies that <see cref="IpAllocationService.IpToUint"/> converts the
	/// address 192.168.1.1 to its expected big-endian uint32 representation
	/// 0xC0A80101 (192=0xC0, 168=0xA8, 1=0x01, 1=0x01).
	/// </summary>
	[Fact]
	public void IpToUint_ConvertsCorrectly()
	{
		// 192.168.1.1 → 0xC0 A8 01 01
		var ip = IPAddress.Parse("192.168.1.1");
		var result = IpAllocationService.IpToUint(ip);
		Assert.Equal(0xC0A80101u, result);
	}

	/// <summary>
	/// Verifies that <see cref="IpAllocationService.UintToIp"/> converts the
	/// value 0xC0A80101 back to the dotted-decimal string "192.168.1.1".
	/// </summary>
	[Fact]
	public void UintToIp_ConvertsCorrectly()
	{
		// 0xC0 A8 01 01 → 192.168.1.1
		var result = IpAllocationService.UintToIp(0xC0A80101u);
		Assert.Equal("192.168.1.1", result.ToString());
	}

	/// <summary>
	/// Verifies that converting an IP address to uint and back produces the
	/// original address unchanged — confirming that the two helpers are inverses.
	/// </summary>
	[Fact]
	public void RoundTrip_IpToUintToIp()
	{
		// Choose an address in the 10/8 range to test a different byte pattern.
		var original = IPAddress.Parse("10.0.0.1");
		var uint32 = IpAllocationService.IpToUint(original);
		var result = IpAllocationService.UintToIp(uint32);

		// Compare dotted-decimal strings because IPAddress.Equals() may differ
		// on IPv4-mapped IPv6 representations.
		Assert.Equal(original.ToString(), result.ToString());
	}
}
