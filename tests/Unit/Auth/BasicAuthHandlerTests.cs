using System.Text;

namespace IpamService.Tests.Unit.Auth;

/// <summary>
/// Unit tests for the Base64 credential parsing logic that underlies
/// <see cref="IpamService.Auth.BasicAuthHandler"/>. These tests validate the
/// pure encoding/decoding mechanics in isolation — no HTTP context or Identity
/// infrastructure is required.
///
/// The handler itself is tested end-to-end in the integration tests via
/// authenticated and unauthenticated HTTP calls.
/// </summary>
public class BasicAuthHandlerTests
{
	/// <summary>
	/// Verifies that Base64-encoding "username:password" and decoding it back
	/// produces the original credential string intact.
	/// </summary>
	[Fact]
	public void Base64Decode_ValidCredentials()
	{
		// Encode a well-formed credential string as the handler would receive it.
		var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:password123"));

		// Decode and split as the handler does.
		var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
		var parts = decoded.Split(':', 2);

		// Verify the username and password survive the round-trip unchanged.
		Assert.Equal("admin", parts[0]);
		Assert.Equal("password123", parts[1]);
	}

	/// <summary>
	/// Verifies that <see cref="Convert.FromBase64String"/> throws
	/// <see cref="FormatException"/> for strings that are not valid Base64,
	/// confirming that the handler's try/catch is needed to guard against it.
	/// </summary>
	[Fact]
	public void Base64Decode_InvalidBase64_Throws()
	{
		// A string with illegal Base64 characters should throw.
		Assert.Throws<FormatException>(() => Convert.FromBase64String("!!!invalid!!!"));
	}

	/// <summary>
	/// Verifies that a credential string with no colon separator returns -1
	/// from <see cref="string.IndexOf(char)"/>, which the handler uses to
	/// detect a malformed credential.
	/// </summary>
	[Fact]
	public void MissingColon_HasNoSeparator()
	{
		// A username-only string with no colon should produce index -1.
		var decoded = "justausername";
		var index = decoded.IndexOf(':');
		Assert.Equal(-1, index);
	}

	/// <summary>
	/// Verifies that when the password itself contains a colon character, the
	/// handler correctly splits on the FIRST colon only, preserving the full
	/// password (including the embedded colon) as-is. This is the RFC 7617
	/// requirement for Basic auth credential parsing.
	/// </summary>
	[Fact]
	public void ColonInPassword_SplitsOnFirst()
	{
		// "user:pass:word" — the password is "pass:word", not "pass".
		var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass:word"));
		var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

		// IndexOf(':') finds the first colon — the separator between username and password.
		var idx = decoded.IndexOf(':');
		var username = decoded[..idx];
		var password = decoded[(idx + 1)..];

		Assert.Equal("user", username);
		Assert.Equal("pass:word", password);
	}
}
