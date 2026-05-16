using System.Net;
using IpamService.Data;
using IpamService.Services;
using Microsoft.EntityFrameworkCore;

namespace IpamService.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="SubnetValidationService"/>. These tests exercise
/// CIDR parsing and RFC1918 classification logic in isolation. The overlap-check
/// method requires a live database and is covered by integration tests.
///
/// Each test creates a fresh in-memory SQLite database via
/// <c>Database.EnsureCreated()</c>. A persistent connection is opened on the
/// context so the in-memory database is not dropped between operations.
/// </summary>
public class SubnetValidationServiceTests
{
	/// <summary>
	/// Builds a <see cref="SubnetValidationService"/> backed by a fresh in-memory
	/// SQLite database. The connection is opened immediately so the database persists
	/// for the lifetime of the context (SQLite in-memory databases are dropped when
	/// the last connection closes).
	/// </summary>
	/// <returns>A ready-to-use <see cref="SubnetValidationService"/> instance.</returns>
	private static SubnetValidationService CreateService()
	{
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		var db = new AppDbContext(options);

		// Keep the connection open so the in-memory database survives for the
		// entire test — SQLite drops an in-memory DB when all connections close.
		db.Database.OpenConnection();
		db.Database.EnsureCreated();

		return new SubnetValidationService(db);
	}

	/// <summary>
	/// Verifies that <see cref="SubnetValidationService.TryParseCidr"/> returns
	/// <c>true</c> for valid CIDR strings and <c>false</c> for invalid ones.
	/// </summary>
	/// <param name="cidr">The CIDR string to parse.</param>
	/// <param name="expected"><c>true</c> if the CIDR should be parseable; <c>false</c> otherwise.</param>
	[Theory]
	[InlineData("192.168.1.0/24", true)]
	[InlineData("10.0.0.0/8", true)]
	[InlineData("not-a-cidr", false)]
	[InlineData("300.0.0.0/24", false)]
	public void TryParseCidr_ValidatesFormat(string cidr, bool expected)
	{
		var svc = CreateService();
		var result = svc.TryParseCidr(cidr, out _);
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Verifies that <see cref="SubnetValidationService.IsRfc1918"/> correctly
	/// classifies networks as private or public according to RFC1918.
	/// Covers all three private ranges (10/8, 172.16/12, 192.168/16) and their
	/// subsets, plus edge cases that fall outside the private ranges.
	/// </summary>
	/// <param name="cidr">The CIDR to evaluate.</param>
	/// <param name="expected"><c>true</c> if the network is RFC1918 private; <c>false</c> otherwise.</param>
	[Theory]
	[InlineData("10.0.0.0/8", true)]
	[InlineData("10.10.0.0/16", true)]
	[InlineData("172.16.0.0/12", true)]
	[InlineData("172.20.0.0/16", true)]
	[InlineData("192.168.0.0/16", true)]
	[InlineData("192.168.1.0/24", true)]
	[InlineData("8.8.8.0/24", false)]      // Public Google DNS range — not RFC1918.
	[InlineData("172.32.0.0/16", false)]   // Just outside 172.16/12 — not RFC1918.
	public void IsRfc1918_ClassifiesCorrectly(string cidr, bool expected)
	{
		var svc = CreateService();

		// Parse the CIDR first; the test data is known-valid so we assert success.
		svc.TryParseCidr(cidr, out var network);

		// .Value is required because TryParseCidr returns IPNetwork? (nullable struct).
		Assert.Equal(expected, svc.IsRfc1918(network!.Value));
	}
}
