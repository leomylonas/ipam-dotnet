using System.Net;
using System.Net.Http.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;

namespace IpamService.Tests.Integration.Controllers;

/// <summary>
/// Abstract base class containing all integration tests for <c>AuthController</c>.
/// Concrete subclasses supply the factory so the same test logic runs against SQLite,
/// MySQL, and PostgreSQL without duplication.
///
/// Tests cover: POST /auth/login (valid credentials, invalid credentials, cookie issuance),
/// POST /auth/logout (clears cookie), and GET /auth/me (Basic auth, cookie auth, unauthenticated).
/// </summary>
public abstract class AuthControllerTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>The ID of the tenancy created during initialisation.</summary>
	private Guid _tenancyId;

	/// <summary>
	/// Initialises a new instance of <see cref="AuthControllerTestsBase"/> using
	/// the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected AuthControllerTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin and a TenantAdmin belonging to a test
	/// tenancy. No HTTP clients are created here — each test creates its own client
	/// with the appropriate auth configuration.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			// GlobalAdmin — no tenancy affiliation.
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = Roles.GlobalAdmin
			};
			await um.CreateAsync(admin, "Test1234!");

			// Tenancy for the TenantAdmin.
			var tenancy = new Tenancy
			{
				Id = Guid.NewGuid(),
				Name = "TestTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			};
			_tenancyId = tenancy.Id;
			db.Tenancies.Add(tenancy);

			// TenantAdmin for that tenancy.
			var tadmin = new ApplicationUser
			{
				UserName = "tadmin",
				Email = "tadmin",
				Role = Roles.TenantAdmin,
				TenancyId = tenancy.Id
			};
			await um.CreateAsync(tadmin, "Test1234!");

			await db.SaveChangesAsync();
		});
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	// ── POST /auth/login ──────────────────────────────────────────────────────

	/// <summary>
	/// Verifies that valid credentials return 200 OK with a correctly populated
	/// <see cref="AuthMeResponse"/> body including role and tenancy information.
	/// </summary>
	[Fact]
	public async Task Login_WithValidAdminCredentials_Returns200AndUserProfile()
	{
		// Use a plain client — no auth header, but cookie container enabled by default.
		var client = Factory.CreateClient();
		var req = new LoginRequest("admin", "Test1234!");

		var response = await client.PostAsJsonAsync("/auth/login", req);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<AuthMeResponse>();
		Assert.NotNull(body);
		Assert.Equal("admin", body.Username);
		Assert.Equal(Roles.GlobalAdmin, body.Role);
		// GlobalAdmin has no tenancy affiliation.
		Assert.Null(body.TenancyId);
	}

	/// <summary>
	/// Verifies that a TenantAdmin's login response includes the correct tenancy ID.
	/// </summary>
	[Fact]
	public async Task Login_WithValidTenantAdminCredentials_Returns200WithTenancyId()
	{
		var client = Factory.CreateClient();
		var req = new LoginRequest("tadmin", "Test1234!");

		var response = await client.PostAsJsonAsync("/auth/login", req);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<AuthMeResponse>();
		Assert.NotNull(body);
		Assert.Equal("tadmin", body.Username);
		Assert.Equal(Roles.TenantAdmin, body.Role);
		// TenantAdmin must have a tenancy ID in the response.
		Assert.Equal(_tenancyId, body.TenancyId);
	}

	/// <summary>
	/// Verifies that an incorrect password results in 401 Unauthorized with no
	/// detail body — credentials are never revealed in error responses.
	/// </summary>
	[Fact]
	public async Task Login_WithWrongPassword_Returns401()
	{
		var client = Factory.CreateClient();
		var req = new LoginRequest("admin", "WrongPassword1!");

		var response = await client.PostAsJsonAsync("/auth/login", req);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a username that does not exist results in 401 Unauthorized,
	/// indistinguishable from a wrong-password failure to prevent user enumeration.
	/// </summary>
	[Fact]
	public async Task Login_WithUnknownUsername_Returns401()
	{
		var client = Factory.CreateClient();
		var req = new LoginRequest("ghost", "Test1234!");

		var response = await client.PostAsJsonAsync("/auth/login", req);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	/// <summary>
	/// Verifies that a successful login sets a cookie that authenticates subsequent
	/// requests on the same client instance (cookie container is shared across
	/// requests within the same <see cref="HttpClient"/>).
	/// </summary>
	[Fact]
	public async Task Login_IssuesCookieThatAuthenticatesSubsequentRequests()
	{
		// A single client shares its cookie container across all requests it makes.
		// No Authorization header is set — authentication must happen via cookie only.
		var client = Factory.CreateClient();

		// POST /auth/login — the server sets the ipam_auth cookie in the response,
		// which the client's cookie container stores automatically.
		var loginResp = await client.PostAsJsonAsync("/auth/login", new LoginRequest("admin", "Test1234!"));
		Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

		// GET /auth/me with no Authorization header — the cookie container sends
		// the stored cookie, so the Combined scheme routes to the Cookie handler.
		var meResp = await client.GetAsync("/auth/me");
		Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
		var body = await meResp.Content.ReadFromJsonAsync<AuthMeResponse>();
		Assert.NotNull(body);
		Assert.Equal("admin", body.Username);
	}

	// ── POST /auth/logout ─────────────────────────────────────────────────────

	/// <summary>
	/// Verifies that logging out clears the authentication cookie so that
	/// subsequent requests to protected endpoints receive 401 Unauthorized.
	/// </summary>
	[Fact]
	public async Task Logout_WhenAuthenticatedViaCookie_Returns204AndClearsCookie()
	{
		// Use a single client so the cookie container is shared.
		var client = Factory.CreateClient();

		// Step 1 — Login to obtain the cookie.
		var loginResp = await client.PostAsJsonAsync("/auth/login", new LoginRequest("admin", "Test1234!"));
		Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

		// Step 2 — Logout. The server sends Set-Cookie with an expired date,
		// which causes the client's cookie container to delete the stored cookie.
		var logoutResp = await client.PostAsync("/auth/logout", null);
		Assert.Equal(HttpStatusCode.NoContent, logoutResp.StatusCode);

		// Step 3 — After logout, /auth/me must return 401 because the cookie has
		// been cleared and no Authorization header is present.
		var meResp = await client.GetAsync("/auth/me");
		Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
	}

	/// <summary>
	/// Verifies that POST /auth/logout returns 204 when authenticated via Basic auth
	/// (the endpoint is [Authorize] regardless of scheme; SignOutAsync is a no-op
	/// when no cookie exists but the request still succeeds).
	/// </summary>
	[Fact]
	public async Task Logout_WhenAuthenticatedViaBasicAuth_Returns204()
	{
		// A Basic-auth client has no cookie — SignOutAsync is a no-op, but the
		// endpoint must still return 204.
		var client = Factory.CreateAuthenticatedClient("admin", "Test1234!");

		var response = await client.PostAsync("/auth/logout", null);

		Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
	}

	/// <summary>
	/// Verifies that POST /auth/logout returns 401 when called without any
	/// authentication, since the endpoint carries [Authorize].
	/// </summary>
	[Fact]
	public async Task Logout_WhenNotAuthenticated_Returns401()
	{
		// Plain client with no cookie and no Authorization header.
		var client = Factory.CreateClient();

		var response = await client.PostAsync("/auth/logout", null);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	// ── GET /auth/me ──────────────────────────────────────────────────────────

	/// <summary>
	/// Verifies that GET /auth/me returns the caller's profile when authenticated
	/// via Basic auth — the standard mechanism for direct API consumers.
	/// </summary>
	[Fact]
	public async Task Me_WithBasicAuth_Returns200AndCorrectProfile()
	{
		var client = Factory.CreateAuthenticatedClient("tadmin", "Test1234!");

		var response = await client.GetAsync("/auth/me");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<AuthMeResponse>();
		Assert.NotNull(body);
		Assert.Equal("tadmin", body.Username);
		Assert.Equal(Roles.TenantAdmin, body.Role);
		// The response must include the user's tenancy ID.
		Assert.Equal(_tenancyId, body.TenancyId);
	}

	/// <summary>
	/// Verifies that GET /auth/me returns the caller's profile when authenticated
	/// via cookie — the mechanism used by the React UI.
	/// </summary>
	[Fact]
	public async Task Me_WithCookieAuth_Returns200AndCorrectProfile()
	{
		// Cookie-based: login first, then call /auth/me without an Authorization header.
		var client = Factory.CreateClient();
		await client.PostAsJsonAsync("/auth/login", new LoginRequest("admin", "Test1234!"));

		var response = await client.GetAsync("/auth/me");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<AuthMeResponse>();
		Assert.NotNull(body);
		Assert.Equal("admin", body.Username);
		Assert.Equal(Roles.GlobalAdmin, body.Role);
	}

	/// <summary>
	/// Verifies that GET /auth/me returns 401 when called with no authentication at all.
	/// </summary>
	[Fact]
	public async Task Me_WithNoAuth_Returns401()
	{
		var client = Factory.CreateClient();

		var response = await client.GetAsync("/auth/me");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
}

/// <summary>
/// Runs <see cref="AuthControllerTestsBase"/> against an isolated SQLite file database.
/// </summary>
public class AuthControllerTests : AuthControllerTestsBase
{
	/// <summary>Initialises the tests with a per-instance SQLite file database.</summary>
	public AuthControllerTests() : base(new TestWebApplicationFactory()) { }
}

/// <summary>
/// Runs <see cref="AuthControllerTestsBase"/> against a MySQL Testcontainer database.
/// </summary>
[Collection("mysql")]
public class AuthControllerMySqlTests : AuthControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a MySQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>mysql</c> collection.</param>
	public AuthControllerMySqlTests(MySqlContainerFixture fixture)
		: base(new MySqlTestWebApplicationFactory(fixture)) { }
}

/// <summary>
/// Runs <see cref="AuthControllerTestsBase"/> against a PostgreSQL Testcontainer database.
/// </summary>
[Collection("postgres")]
public class AuthControllerPostgresTests : AuthControllerTestsBase
{
	/// <summary>
	/// Initialises the tests with a PostgreSQL-backed factory.
	/// </summary>
	/// <param name="fixture">Injected by xUnit from the <c>postgres</c> collection.</param>
	public AuthControllerPostgresTests(PostgresContainerFixture fixture)
		: base(new PostgresTestWebApplicationFactory(fixture)) { }
}
