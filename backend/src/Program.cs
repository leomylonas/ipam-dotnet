using IpamService.Auth;
using IpamService.Config;
using IpamService.Data;
using IpamService.Models;
using IpamService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

// ── Composition root ─────────────────────────────────────────────────────────
// Everything before app.Build() registers services into the DI container.
// The order of registration matters when services depend on each other.

var builder = WebApplication.CreateBuilder(args);

// Shorthand reference so we don't have to write builder.Configuration everywhere.
var config = builder.Configuration;

// ── Logging (Serilog) ─────────────────────────────────────────────────────────
// Replace the default Microsoft.Extensions.Logging pipeline with Serilog.
// Minimum levels are read from the "Serilog" configuration section so they can
// be adjusted via appsettings.json or environment variables without redeployment.
// The sink pipeline (async-wrapped console) and enrichers are hard-coded here
// because they are infrastructure concerns, not operational tunables.
builder.Host.UseSerilog((ctx, cfg) =>
	cfg
		.ReadFrom.Configuration(ctx.Configuration)
		.Enrich.FromLogContext()
		.WriteTo.Async(a => a.Console()));

// ── Database provider selection ───────────────────────────────────────────────
// The provider and connection string are read from configuration so that the
// same binary can target SQLite (local dev / tests), MySQL (self-hosted), or
// PostgreSQL (cloud) without code changes.
var provider = config["Database:Provider"];
var connStr = config["Database:ConnectionString"];

switch (provider)
{
	case "sqlite":
		builder.Services.AddDbContext<AppDbContext, SqliteAppDbContext>(options =>
			options.UseSqlite(connStr, x => x.MigrationsAssembly("IpamService")));
		break;

	case "mysql":
		// Oracle MySQL provider — UseMySQL (capital SQL) is the Oracle API,
		// distinct from Pomelo's UseMySql. No ServerVersion needed.
		builder.Services.AddDbContext<AppDbContext, MySqlAppDbContext>(options =>
			options.UseMySQL(connStr,
				x => x.MigrationsAssembly("IpamService")));
		break;

	case "postgres":
		// Npgsql provider for PostgreSQL and compatible databases.
		builder.Services.AddDbContext<AppDbContext, PostgresAppDbContext>(options =>
			options.UseNpgsql(connStr,
				x => x.MigrationsAssembly("IpamService")));
		break;

	default:
		// Fail fast at startup rather than producing a confusing runtime
		// error the first time the database is touched.
		throw new InvalidOperationException($"Unknown database provider: {provider}");
}

// ── ASP.NET Identity ──────────────────────────────────────────────────────────
// AddIdentity wires up user/role stores backed by EF Core, the password hasher,
// user validators, and all the UserManager/SignInManager infrastructure.
// IMPORTANT: AddIdentity also sets DefaultAuthenticateScheme to the cookie
// scheme (IdentityConstants.ApplicationScheme). We override that below in
// AddAuthentication so that our custom scheme governs every request instead.
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
	// Enforce a sensible minimum password policy — at least 8 characters
	// with at least one digit, one uppercase, and one lowercase letter.
	options.Password.RequireDigit = true;
	options.Password.RequiredLength = 8;
	options.Password.RequireNonAlphanumeric = false;
	options.Password.RequireUppercase = true;
	options.Password.RequireLowercase = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ── Authentication ────────────────────────────────────────────────────────────
// The API supports two auth schemes that coexist without interfering:
//
//   "Basic"    — stateless per-request credential validation. Used by all
//                direct API consumers (scripts, tools, other services). The
//                Authorization: Basic header is checked on every request.
//
//   "Cookie"   — encrypted ASP.NET Core cookie issued by POST /auth/login.
//                Used by the React SPA so the browser handles credentials
//                automatically after a login form submission.
//
//   "Combined" — a PolicyScheme that routes to "Basic" when an Authorization
//                header is present, and to "Cookie" otherwise. This is set as
//                the default authenticate scheme so all [Authorize] endpoints
//                accept either mechanism transparently.
//
// DefaultChallengeScheme and DefaultForbidScheme are both set to "Combined"
// so that challenge routing uses the same ForwardDefaultSelector as
// authentication. This means:
//   - Requests with an Authorization header → Basic challenge (WWW-Authenticate
//     header sent; correct for API clients such as curl and Postman).
//   - Requests without an Authorization header → Cookie challenge (plain 401,
//     no WWW-Authenticate header; prevents the browser from showing its
//     native Basic-auth dialog when the SPA's startup /auth/me check fails).
//
// NOTE: ASP.NET Core Data Protection keys used to encrypt/decrypt the cookie
// are ephemeral (in-memory) by default. For production deployments with
// multiple instances or container restarts, persist the keys via
// AddDataProtection().PersistKeysToFileSystem() or a cloud key store so that
// existing cookies remain valid across restarts.
builder.Services.AddAuthentication(options =>
{
	// Route all authentication, challenge, and forbid checks through the
	// Combined policy scheme so the Basic/Cookie split is applied uniformly.
	options.DefaultAuthenticateScheme = AuthConstants.Schemes.Combined;
	options.DefaultChallengeScheme = AuthConstants.Schemes.Combined;
	options.DefaultForbidScheme = AuthConstants.Schemes.Combined;
})
.AddScheme<AuthenticationSchemeOptions, BasicAuthHandler>(AuthConstants.Schemes.Basic, null)
.AddCookie(AuthConstants.Schemes.Cookie, options =>
{
	// Cookie name surfaced in the browser — no functional impact but makes
	// it easy to identify in browser dev tools.
	options.Cookie.Name = AuthConstants.Cookies.AuthCookieName;

	// HttpOnly prevents JavaScript from reading the cookie value, mitigating
	// XSS-based session theft.
	options.Cookie.HttpOnly = true;

	// SameSite=Strict prevents the cookie from being sent on cross-site requests,
	// providing CSRF protection without requiring a separate CSRF token.
	options.Cookie.SameSite = SameSiteMode.Strict;

	// SameAsRequest allows the cookie over HTTP in development (Vite dev server)
	// while automatically requiring HTTPS when the app is served over HTTPS.
	options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

	// Session cookie — expires when the browser session ends rather than
	// persisting across browser restarts. Set ExpireTimeSpan to make it
	// a sliding persistent cookie if longer sessions are required.
	options.Cookie.MaxAge = TimeSpan.FromHours(24);
	options.SlidingExpiration = true;

	// Suppress the default MVC redirects — this is an API, not an MVC app.
	// Return HTTP status codes directly instead of redirecting to /Account/Login.
	options.Events.OnRedirectToLogin = ctx =>
	{
		ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
		return Task.CompletedTask;
	};
	options.Events.OnRedirectToAccessDenied = ctx =>
	{
		ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
		return Task.CompletedTask;
	};
})
.AddPolicyScheme(AuthConstants.Schemes.Combined, "Basic or Cookie", options =>
{
	// Route to Basic auth when an Authorization header is present (API clients),
	// and to the Cookie scheme otherwise (browser-based UI requests).
	// This means a request with both a cookie AND an Authorization header will
	// always use Basic auth, which is the correct behaviour for hybrid clients.
	options.ForwardDefaultSelector = ctx =>
		ctx.Request.Headers.ContainsKey("Authorization")
			? AuthConstants.Schemes.Basic
			: AuthConstants.Schemes.Cookie;
});

// ── Authorization ─────────────────────────────────────────────────────────────
// Enables [Authorize] and [Authorize(Roles = "...")] on controllers.
// No additional policies are defined — role-based checks are done inline.
builder.Services.AddAuthorization();

// ── Domain services ───────────────────────────────────────────────────────────
// Scoped lifetime: one instance per HTTP request, shared between all services
// and controllers within that request so they operate on the same EF context.
// Core algorithmic services (pre-existing):
builder.Services.AddScoped<IpAllocationService>();
builder.Services.AddScoped<SubnetValidationService>();
builder.Services.AddScoped<AuditService>();
// Domain-area services — each owns the business logic and DB access for its area:
builder.Services.AddScoped<TenancyService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SubnetService>();
builder.Services.AddScoped<ExclusionService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<StatsService>();
// Dashboard service — aggregates data across multiple resources for the UI:
builder.Services.AddScoped<DashboardService>();

// ── Configuration bindings ────────────────────────────────────────────────────
// Bind typed options classes so services can receive config via IOptions<T>.
builder.Services.Configure<SeedOptions>(config.GetSection("Seed"));
builder.Services.Configure<DashboardOptions>(config.GetSection("Dashboard"));
builder.Services.Configure<UiOptions>(config.GetSection("Ui"));

// ── Web API infrastructure ────────────────────────────────────────────────────
builder.Services.AddControllers();

// AddProblemDetails registers the RFC 7807 / RFC 9457 problem-details factory
// used by the global exception handler below and by ControllerBase.Problem().
builder.Services.AddProblemDetails();

// OpenAPI document generation — used by Scalar in Development only.
builder.Services.AddOpenApi();

// ── Build the application ─────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Only expose the OpenAPI document and the interactive Scalar UI in Development.
// This prevents accidental exposure in production where the schema could reveal
// internal API surface.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
	app.MapScalarApiReference();
}

// ── Global exception handler ──────────────────────────────────────────────────
// Catches any exception that escapes the controller pipeline (i.e. is not
// handled by IpamControllerBase.ExecuteAsync) and converts it into a Problem
// Details response (RFC 7807 / RFC 9457) so clients always receive structured
// JSON rather than an unformatted 500 HTML page.
//
// UseExceptionHandler with a delegate is the preferred approach on .NET 8+:
// the delegate receives an IExceptionHandlerFeature that exposes the raw
// exception, and we write a Problem response using IProblemDetailsService
// which is registered above via AddProblemDetails().
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
	// Retrieve the exception that triggered this handler.
	var exceptionFeature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
	var ex = exceptionFeature?.Error;

	// Default to 500; the status has already been set to 500 by the middleware.
	ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;

	// IProblemDetailsService writes the structured application/problem+json body.
	// We pass the exception so that the factory can include it when
	// configured to do so (e.g. in Development via exception-detail middleware).
	var problemDetailsService = ctx.RequestServices.GetRequiredService<IProblemDetailsService>();
	await problemDetailsService.WriteAsync(new ProblemDetailsContext
	{
		HttpContext = ctx,
		// Populate the detail field with the exception message so that
		// server-side errors surface a brief human-readable description.
		// In production, consider clearing this to avoid leaking internals.
		ProblemDetails =
		{
			Status = StatusCodes.Status500InternalServerError,
			Title = "An unexpected error occurred.",
			Detail = ex?.Message,
		},
		Exception = ex,
	});
}));

// Authentication must run before authorization so that HttpContext.User is
// populated before [Authorize] attributes are evaluated.
app.UseAuthentication();
app.UseAuthorization();

// Register all controller routes discovered by reflection.
app.MapControllers();

// ── Health endpoint ───────────────────────────────────────────────────────────
// A simple liveness + DB connectivity check that can be polled by container
// orchestrators or load balancers. Not protected by authentication so that
// the probe does not need credentials.
app.MapGet("/health", async (AppDbContext db) =>
{
	try
	{
		// CanConnectAsync opens a brief connection to verify the DB is reachable.
		await db.Database.CanConnectAsync();
		return Results.Ok(new { status = "healthy", database = "connected" });
	}
	catch
	{
		// Return 200 with an "unhealthy" payload rather than 500 so load
		// balancers that only check for a non-5xx response still receive a
		// signal they can parse.
		return Results.Ok(new { status = "unhealthy", database = "disconnected" });
	}
});

// ── React SPA static file serving ────────────────────────────────────────────
// When Ui:Enabled is true (the default), serve the built React app from
// wwwroot/ and fall back to index.html for all non-API, non-file requests so
// that client-side routing works correctly.
//
// When Ui:Enabled is false, these registrations are skipped and the server
// operates in API-only mode — useful in environments where the SPA is hosted
// on a CDN or a separate static file server.
var uiEnabled = config.GetSection("Ui").GetValue<bool?>("Enabled") ?? true;
if (uiEnabled)
{
	// Serve files from wwwroot/ (default static file root). The Vite build
	// output should be copied here by the Dockerfile or CI pipeline.
	app.UseStaticFiles();

	// For any request that does not match an API route or a static file,
	// serve index.html so the React router handles the URL client-side.
	// The /api and /auth and /dashboard prefixes are excluded implicitly
	// because MapControllers() registers those routes before this fallback.
	app.MapFallbackToFile("index.html");
}

// ── Startup migration and seed ────────────────────────────────────────────────
// Run pending EF Core migrations synchronously before accepting traffic.
// This ensures the schema is always up to date when the service starts,
// which is safe for single-instance deployments; for multi-instance you would
// use a separate migration job instead.
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

	// Apply any migrations that have not yet been applied to this database.
	db.Database.Migrate();

	// Seed the GlobalAdmin user from configuration if it does not already exist.
	// After the first run the seed values are no longer read; changing them will
	// NOT update an existing admin's password — use PUT /api/users/{id} for that.
	var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
	var seedOptions = config.GetSection("Seed");
	var adminUsername = seedOptions["AdminUsername"]!;
	var adminPassword = seedOptions["AdminPassword"]!;

	if (await userManager.FindByNameAsync(adminUsername) is null)
	{
		// Create the GlobalAdmin with no tenancy affiliation.
		var admin = new ApplicationUser
		{
			UserName = adminUsername,
			Email = adminUsername,
			Role = Roles.GlobalAdmin,
			TenancyId = null
		};
		var createResult = await userManager.CreateAsync(admin, adminPassword);
		if (!createResult.Succeeded)
		{
			// Fail fast so a misconfigured seed password surfaces immediately
			// rather than leaving the application in a state with no admin user.
			var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
			throw new InvalidOperationException($"Failed to seed GlobalAdmin user: {errors}");
		}
	}
}

app.Run();

// ── Test entry point ──────────────────────────────────────────────────────────
// The partial declaration makes Program visible to the test project so that
// WebApplicationFactory<Program> can locate and boot the application.
public partial class Program { }
