using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Integration tests covering TLS/HTTPS behaviour and the <c>UseForwardedHeaders</c>
/// middleware registered in <c>Program.cs</c>.
///
/// Three scenarios are exercised:
/// <list type="number">
///   <item><description>
///     The app responds correctly when the test server treats the connection as
///     HTTPS (simulating Kestrel serving TLS directly).
///   </description></item>
///   <item><description>
///     When a trusted reverse proxy passes <c>X-Forwarded-Proto: https</c>,
///     <c>UseForwardedHeaders</c> upgrades <c>Request.Scheme</c> to <c>https</c>,
///     which causes the auth cookie to carry the <c>Secure</c> attribute
///     (the cookie is configured with <c>CookieSecurePolicy.SameAsRequest</c>).
///   </description></item>
///   <item><description>
///     Without <c>X-Forwarded-Proto</c>, a plain HTTP request causes the auth
///     cookie to be issued <em>without</em> the <c>Secure</c> attribute — the
///     expected behaviour for a local / direct HTTP deployment.
///   </description></item>
/// </list>
///
/// Note on <c>X-Forwarded-For</c>: because the app is configured with both
/// <c>ForwardedHeaders.XForwardedFor</c> and <c>ForwardedHeaders.XForwardedProto</c>,
/// the middleware loop that applies <c>X-Forwarded-Proto</c> only executes when
/// <c>X-Forwarded-For</c> also contains at least one entry. The forwarded-proto
/// tests therefore include both headers, matching what a real reverse proxy sends.
/// </summary>
public class HttpsTests : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	private readonly TestWebApplicationFactory _factory;

	/// <summary>
	/// Initialises a new instance of <see cref="HttpsTests"/> with a fresh
	/// per-instance SQLite database.
	/// </summary>
	public HttpsTests()
	{
		_factory = new TestWebApplicationFactory();
	}

	/// <summary>
	/// Seeds a GlobalAdmin user so login requests have valid credentials to
	/// use when asserting cookie behaviour.
	/// </summary>
	public async Task InitializeAsync()
	{
		await _factory.SeedDatabaseAsync(async (db, um) =>
		{
			// GlobalAdmin — no tenancy affiliation; used by all tests in this class.
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = Roles.GlobalAdmin
			};
			await um.CreateAsync(admin, "Test1234!");
		});
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		_factory.Dispose();
		return Task.CompletedTask;
	}

	// ── Direct HTTPS via test server ──────────────────────────────────────────

	/// <summary>
	/// Verifies that the application responds to HTTPS requests. The test server
	/// is placed into HTTPS mode by setting the client's base address to
	/// <c>https://localhost</c>, which causes the in-process handler to set
	/// <c>Request.Scheme = "https"</c> on every request it receives.
	/// </summary>
	[Fact]
	public async Task HealthEndpoint_RespondsWith200_OverHttps()
	{
		// Setting BaseAddress to https://localhost tells the TestServer handler
		// to treat every request as HTTPS without requiring a real certificate.
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			BaseAddress = new Uri("https://localhost")
		});

		var response = await client.GetAsync("/health");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	/// <summary>
	/// Verifies that when the test server treats the connection as HTTPS, the auth
	/// cookie issued by <c>POST /auth/login</c> carries the <c>Secure</c> attribute.
	/// This confirms that <c>CookieSecurePolicy.SameAsRequest</c> correctly marks
	/// the cookie as HTTPS-only when <c>Request.Scheme</c> is <c>https</c>.
	/// </summary>
	[Fact]
	public async Task Login_OverHttps_IssuesCookieWithSecureAttribute()
	{
		// https:// base address → TestServer sets Request.IsHttps = true.
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			BaseAddress = new Uri("https://localhost"),
			// Disable auto-redirect so the raw Set-Cookie header is inspectable.
			AllowAutoRedirect = false
		});

		var response = await client.PostAsJsonAsync("/auth/login",
			new LoginRequest("admin", "Test1234!"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		// The Set-Cookie header must include the Secure attribute when the request
		// scheme is https and CookieSecurePolicy is SameAsRequest.
		var setCookieValues = response.Headers.TryGetValues("Set-Cookie", out var values)
			? values.ToList()
			: new List<string>();

		Assert.True(setCookieValues.Count > 0,
			"Expected a Set-Cookie header in the login response");
		Assert.True(
			setCookieValues.Any(v => v.Contains("secure", StringComparison.OrdinalIgnoreCase)),
			"Auth cookie must carry the Secure attribute when Request.Scheme is https");
	}

	// ── X-Forwarded-Proto via reverse proxy ───────────────────────────────────

	/// <summary>
	/// Verifies that <c>UseForwardedHeaders</c> honours <c>X-Forwarded-Proto: https</c>
	/// sent alongside <c>X-Forwarded-For</c> (as a real reverse proxy would), causing
	/// the app to treat the request as HTTPS. The auth cookie issued by
	/// <c>POST /auth/login</c> must carry the <c>Secure</c> attribute as a result.
	///
	/// The in-process <c>TestServer</c> connection has no <c>RemoteIpAddress</c> (null),
	/// which the <c>ForwardedHeadersMiddleware</c> treats as a trusted source, so the
	/// forwarded headers are applied without needing to configure additional
	/// <c>KnownNetworks</c> entries.
	/// </summary>
	[Fact]
	public async Task Login_WithXForwardedProtoHttps_IssuesCookieWithSecureAttribute()
	{
		// Plain HTTP client — the forwarded header is what upgrades the scheme.
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});

		var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
		{
			Content = JsonContent.Create(new LoginRequest("admin", "Test1234!"))
		};

		// Simulate a reverse proxy that terminates TLS and forwards both the
		// original client IP and the original protocol. Both headers are required:
		// ForwardedHeadersMiddleware only applies X-Forwarded-Proto when the loop
		// has at least one X-Forwarded-For entry to process (because the app is
		// configured with both ForwardedHeaders.XForwardedFor | XForwardedProto).
		//
		// 203.0.113.1 is from the TEST-NET-3 documentation range (RFC 5737) and
		// will never be a real address that conflicts with KnownNetworks validation.
		request.Headers.Add("X-Forwarded-For", "203.0.113.1");
		request.Headers.Add("X-Forwarded-Proto", "https");

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		// The scheme upgrade should cause the cookie to be marked Secure.
		var setCookieValues = response.Headers.TryGetValues("Set-Cookie", out var values)
			? values.ToList()
			: new List<string>();

		Assert.True(setCookieValues.Count > 0,
			"Expected a Set-Cookie header in the login response");
		Assert.True(
			setCookieValues.Any(v => v.Contains("secure", StringComparison.OrdinalIgnoreCase)),
			"Auth cookie must carry the Secure attribute when X-Forwarded-Proto: https is present");
	}

	/// <summary>
	/// Verifies that without <c>X-Forwarded-Proto</c>, the auth cookie is issued
	/// without the <c>Secure</c> attribute when the request is plain HTTP.
	/// This is the expected behaviour for local development and direct HTTP deployments
	/// and confirms that <c>CookieSecurePolicy.SameAsRequest</c> does not upgrade
	/// cookies unconditionally.
	/// </summary>
	[Fact]
	public async Task Login_OverPlainHttp_IssuesCookieWithoutSecureAttribute()
	{
		// Default (http) client with no forwarded headers — scheme stays http.
		var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});

		var response = await client.PostAsJsonAsync("/auth/login",
			new LoginRequest("admin", "Test1234!"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		// The cookie must NOT have Secure when the request is plain HTTP.
		var setCookieValues = response.Headers.TryGetValues("Set-Cookie", out var values)
			? values.ToList()
			: new List<string>();

		Assert.True(setCookieValues.Count > 0,
			"Expected a Set-Cookie header in the login response");
		Assert.False(
			setCookieValues.Any(v => v.Contains("secure", StringComparison.OrdinalIgnoreCase)),
			"Auth cookie must NOT carry the Secure attribute when the request is plain HTTP");
	}
}
