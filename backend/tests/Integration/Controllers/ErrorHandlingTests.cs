using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IpamService.Models;
using IpamService.Models.DTOs;
using IpamService.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace IpamService.Tests.Integration.Controllers;

// ── Test-only controller ─────────────────────────────────────────────────────

/// <summary>
/// A test-only controller that is added to the application via
/// <see cref="ErrorHandlingTestWebApplicationFactory"/> using
/// <c>AddApplicationPart</c>. Its sole purpose is to throw an unhandled
/// exception through the full ASP.NET pipeline so tests can verify that
/// <c>UseExceptionHandler</c> converts it into a Problem Details 500 response.
///
/// This controller is never registered in production; it only exists in the
/// test assembly and is injected during test host construction.
/// </summary>
[ApiController]
[Route("api/test")]
public class ThrowingController : ControllerBase
{
	/// <summary>
	/// Throws an <see cref="InvalidOperationException"/> that intentionally
	/// escapes the controller pipeline without being caught by
	/// <c>IpamControllerBase.ExecuteAsync</c>, so the global exception handler
	/// is the only thing that can intercept it.
	/// </summary>
	/// <returns>Never returns — always throws.</returns>
	[HttpGet("throw")]
	[AllowAnonymous]
	public IActionResult Throw() =>
		// Direct throw, not wrapped in ExecuteAsync — forces the global handler.
		throw new InvalidOperationException("Deliberate test exception for error-handler verification");
}

// ── Factory subclass ─────────────────────────────────────────────────────────

/// <summary>
/// A <see cref="TestWebApplicationFactory"/> subclass that also registers the
/// test assembly as an MVC application part so that <see cref="ThrowingController"/>
/// is discovered alongside the production controllers.
/// </summary>
public class ErrorHandlingTestWebApplicationFactory : TestWebApplicationFactory
{
	/// <inheritdoc/>
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Run the base factory setup first (provider override, seed suppression, etc.).
		base.ConfigureWebHost(builder);

		builder.ConfigureServices(services =>
		{
			// AddControllers() is idempotent — calling it a second time here only
			// affects the returned IMvcBuilder; it does not duplicate registrations.
			// AddApplicationPart makes ASP.NET discover ThrowingController which
			// lives in the test assembly, not the production assembly.
			services.AddControllers()
				.AddApplicationPart(typeof(ThrowingController).Assembly);
		});
	}
}

// ── Test base class ──────────────────────────────────────────────────────────

/// <summary>
/// Integration tests verifying that every typed service exception handled by
/// <c>IpamControllerBase.ExecuteAsync</c> produces an RFC 7807 Problem Details
/// response (correct HTTP status, <c>application/problem+json</c> content type,
/// and the expected JSON fields), and that unhandled exceptions are caught by
/// the global <c>UseExceptionHandler</c> middleware and also produce Problem
/// Details rather than an HTML error page or an empty response.
///
/// Concrete subclasses supply the factory so the same test logic runs against
/// every supported database provider.
/// </summary>
public abstract class ErrorHandlingTestsBase : IAsyncLifetime
{
	/// <summary>Shared factory that owns the test web server and database.</summary>
	protected readonly TestWebApplicationFactory Factory;

	/// <summary>HTTP client pre-authenticated as GlobalAdmin.</summary>
	private HttpClient _adminClient = null!;

	/// <summary>The tenancy ID seeded during <see cref="InitializeAsync"/>.</summary>
	private readonly Guid _tenancyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

	/// <summary>
	/// Initialises a new instance of <see cref="ErrorHandlingTestsBase"/>
	/// using the supplied provider-specific factory.
	/// </summary>
	/// <param name="factory">Factory that controls which database engine is used.</param>
	protected ErrorHandlingTestsBase(TestWebApplicationFactory factory)
	{
		Factory = factory;
	}

	/// <summary>
	/// Seeds the database with a GlobalAdmin user and one tenancy that some tests
	/// use as context (e.g. private subnet creation). Called before each test.
	/// </summary>
	public async Task InitializeAsync()
	{
		await Factory.SeedDatabaseAsync(async (db, um) =>
		{
			// GlobalAdmin used by every test that needs authentication.
			var admin = new ApplicationUser
			{
				UserName = "admin",
				Email = "admin",
				Role = Roles.GlobalAdmin
			};
			await um.CreateAsync(admin, "Test1234!");

			// Tenancy used for tests that need an existing tenancy context
			// (e.g. private subnet creation to trigger ValidationException).
			db.Tenancies.Add(new Tenancy
			{
				Id = _tenancyId,
				Name = "TestTenancy",
				Description = "",
				CreatedAt = DateTime.UtcNow
			});
			await db.SaveChangesAsync();
		});

		_adminClient = Factory.CreateAuthenticatedClient("admin", "Test1234!");
	}

	/// <summary>Disposes the factory after all tests in the class have run.</summary>
	public Task DisposeAsync()
	{
		Factory.Dispose();
		return Task.CompletedTask;
	}

	/// <summary>
	/// Parses the response body as a <see cref="JsonDocument"/> for field-level
	/// assertions. The caller is responsible for disposing the document.
	/// </summary>
	/// <param name="response">The HTTP response whose body to parse.</param>
	/// <returns>A <see cref="JsonDocument"/> representing the response body.</returns>
	private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
	{
		var json = await response.Content.ReadAsStringAsync();
		return JsonDocument.Parse(json);
	}

	// ── NotFoundException → 404 ──────────────────────────────────────────────

	/// <summary>
	/// Verifies that a <c>NotFoundException</c> thrown by a service is caught by
	/// <c>ExecuteAsync</c> and converted to a 404 Problem Details response with
	/// the correct content type and <c>status</c> field.
	///
	/// Triggered by attempting to delete a tenancy that does not exist.
	/// </summary>
	[Fact]
	public async Task NotFoundException_Returns404_WithProblemDetails()
	{
		// A random GUID that will never match a tenancy in the DB.
		var response = await _adminClient.DeleteAsync($"/api/tenancies/{Guid.NewGuid()}");

		// Status code and content type.
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

		// Body must be a valid Problem Details object with the correct status field.
		using var doc = await ReadProblemAsync(response);
		Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
	}

	// ── ConflictException → 409 ─────────────────────────────────────────────

	/// <summary>
	/// Verifies that a <c>ConflictException</c> thrown by a service is caught by
	/// <c>ExecuteAsync</c> and converted to a 409 Problem Details response, with
	/// the conflict message surfaced in the <c>detail</c> field.
	///
	/// Triggered by creating two tenancies with the same name.
	/// </summary>
	[Fact]
	public async Task ConflictException_Returns409_WithProblemDetails()
	{
		// Create the first tenancy.
		await _adminClient.PostAsJsonAsync("/api/tenancies",
			new CreateTenancyRequest("DupTenancy", "Desc", "conflict-user1", "Test1234!"));

		// A second request with the same name triggers ConflictException in TenancyService.
		var response = await _adminClient.PostAsJsonAsync("/api/tenancies",
			new CreateTenancyRequest("DupTenancy", "Desc", "conflict-user2", "Test1234!"));

		// Status code and content type.
		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

		// Body must carry the conflict description in the detail field.
		using var doc = await ReadProblemAsync(response);
		var root = doc.RootElement;
		Assert.Equal(409, root.GetProperty("status").GetInt32());
		Assert.True(root.TryGetProperty("detail", out _), "detail field must be present in 409 Problem");
	}

	// ── ValidationException → 400 ────────────────────────────────────────────

	/// <summary>
	/// Verifies that a <c>ValidationException</c> thrown by a service is caught by
	/// <c>ExecuteAsync</c> and converted to a 400 Problem Details response, with
	/// the validation message surfaced in the <c>detail</c> field.
	///
	/// Triggered by submitting a private subnet with an invalid CIDR string.
	/// </summary>
	[Fact]
	public async Task ValidationException_Returns400_WithProblemDetails()
	{
		// "not-a-cidr" is not valid CIDR notation; SubnetService throws ValidationException.
		var response = await _adminClient.PostAsJsonAsync(
			$"/api/tenancies/{_tenancyId}/subnets",
			new CreateSubnetRequest("not-a-cidr", "Bad Subnet", ""));

		// Status code and content type.
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

		// Body must carry the validation description in the detail field.
		using var doc = await ReadProblemAsync(response);
		var root = doc.RootElement;
		Assert.Equal(400, root.GetProperty("status").GetInt32());
		Assert.True(root.TryGetProperty("detail", out _), "detail field must be present in 400 Problem");
	}

	// ── IdentityOperationException → 400 with errors extension ──────────────

	/// <summary>
	/// Verifies that an <c>IdentityOperationException</c> thrown by a service is
	/// caught by <c>ExecuteAsync</c> and converted to a 400 Problem Details
	/// response, with ASP.NET Identity error descriptions surfaced under the
	/// custom <c>errors</c> extension field.
	///
	/// Triggered by supplying a password that violates the Identity password policy.
	/// </summary>
	[Fact]
	public async Task IdentityOperationException_Returns400_WithErrorsExtension()
	{
		// "weak" fails RequireDigit, RequireUppercase, and RequiredLength checks —
		// Identity will reject it and TenancyService will throw IdentityOperationException.
		var response = await _adminClient.PostAsJsonAsync("/api/tenancies",
			new CreateTenancyRequest("IdErrTenancy", "Desc", "identity-admin", "weak"));

		// Status code and content type.
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

		// Body must have the errors extension populated with at least one Identity error.
		using var doc = await ReadProblemAsync(response);
		var root = doc.RootElement;
		Assert.Equal(400, root.GetProperty("status").GetInt32());
		Assert.True(root.TryGetProperty("errors", out var errors),
			"errors extension must be present in IdentityOperationException Problem response");
		Assert.True(errors.GetArrayLength() > 0, "errors array must contain at least one Identity error description");
	}

	// ── Unhandled exception → 500 ─────────────────────────────────────────────

	/// <summary>
	/// Verifies that an exception which escapes the controller pipeline entirely
	/// (i.e. is not caught by <c>ExecuteAsync</c>) is caught by the global
	/// <c>UseExceptionHandler</c> middleware and converted to a 500 Problem
	/// Details response rather than an HTML error page or an empty body.
	///
	/// Triggered by the test-only <c>ThrowingController.Throw</c> endpoint which
	/// is injected into the test host via <see cref="ErrorHandlingTestWebApplicationFactory"/>.
	/// </summary>
	[Fact]
	public async Task UnhandledException_Returns500_WithProblemDetails()
	{
		// ThrowingController is decorated with [AllowAnonymous] so no auth header is needed.
		var client = Factory.CreateClient();
		var response = await client.GetAsync("/api/test/throw");

		// Status code and content type.
		Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

		// Body must be a Problem Details object with status 500 and a title.
		using var doc = await ReadProblemAsync(response);
		var root = doc.RootElement;
		Assert.Equal(500, root.GetProperty("status").GetInt32());
		Assert.True(root.TryGetProperty("title", out _), "title field must be present in 500 Problem");
	}
}

// ── Concrete test class (SQLite) ─────────────────────────────────────────────

/// <summary>
/// Runs <see cref="ErrorHandlingTestsBase"/> against an isolated SQLite file
/// database — the default for local development and CI.
/// </summary>
public class ErrorHandlingTests : ErrorHandlingTestsBase
{
	/// <summary>
	/// Initialises the tests with an <see cref="ErrorHandlingTestWebApplicationFactory"/>
	/// backed by a fresh per-instance SQLite file database. The specialised factory
	/// is required (rather than the base <c>TestWebApplicationFactory</c>) because
	/// it registers the <see cref="ThrowingController"/> needed by the 500 test.
	/// </summary>
	public ErrorHandlingTests() : base(new ErrorHandlingTestWebApplicationFactory()) { }
}
