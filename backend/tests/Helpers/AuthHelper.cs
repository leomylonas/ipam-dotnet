using System.Net.Http.Headers;
using System.Text;

namespace IpamService.Tests.Helpers;

/// <summary>
/// Extension methods for setting up HTTP Basic Authentication headers on
/// <see cref="HttpClient"/> instances used in tests. Using an extension method
/// keeps test setup code concise without requiring a dedicated auth client wrapper.
/// </summary>
public static class AuthHelper
{
	/// <summary>
	/// Sets the <c>Authorization: Basic</c> header on the client using the
	/// supplied credentials. The credentials are Base64-encoded in the format
	/// <c>username:password</c> as specified by RFC 7617.
	///
	/// This modifies <see cref="HttpClient.DefaultRequestHeaders"/> so all
	/// subsequent requests from this client will include the auth header.
	/// </summary>
	/// <param name="client">The <see cref="HttpClient"/> to configure.</param>
	/// <param name="username">The username to encode into the auth header.</param>
	/// <param name="password">The password to encode into the auth header.</param>
	public static void SetBasicAuth(this HttpClient client, string username, string password)
	{
		// Encode "username:password" as Base64 per the Basic auth specification.
		var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
	}
}
