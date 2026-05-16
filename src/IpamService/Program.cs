using IpamService.Auth;
using IpamService.Config;
using IpamService.Data;
using IpamService.Models;
using IpamService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

// ── Composition root ─────────────────────────────────────────────────────────
// Everything before app.Build() registers services into the DI container.
// The order of registration matters when services depend on each other.

var builder = WebApplication.CreateBuilder(args);

// Shorthand reference so we don't have to write builder.Configuration everywhere.
var config = builder.Configuration;

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
		// Pomelo provider; ServerVersion.AutoDetect queries the server
		// on startup to pick the right SQL dialect.
		builder.Services.AddDbContext<AppDbContext, MySqlAppDbContext>(options =>
			options.UseMySql(connStr, ServerVersion.AutoDetect(connStr),
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
// AddAuthentication so that Basic auth governs every request instead.
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
// Because AddIdentity overrides the default schemes to cookies we must
// explicitly set all three relevant defaults to our "Basic" scheme using
// the lambda overload. Passing "Basic" as the single string argument to
// AddAuthentication() only sets DefaultScheme (a fallback), which AddIdentity
// then overrides — that approach produces 401 on every authenticated request.
builder.Services.AddAuthentication(options =>
{
	options.DefaultAuthenticateScheme = "Basic";
	options.DefaultChallengeScheme = "Basic";
	options.DefaultForbidScheme = "Basic";
})
.AddScheme<AuthenticationSchemeOptions, BasicAuthHandler>("Basic", null);

// ── Authorization ─────────────────────────────────────────────────────────────
// Enables [Authorize] and [Authorize(Roles = "...")] on controllers.
// No additional policies are defined — role-based checks are done inline.
builder.Services.AddAuthorization();

// ── Domain services ───────────────────────────────────────────────────────────
// Scoped lifetime: one instance per HTTP request, shared between all services
// and controllers within that request so they operate on the same EF context.
builder.Services.AddScoped<IpAllocationService>();
builder.Services.AddScoped<SubnetValidationService>();
builder.Services.AddScoped<AuditService>();

// ── Web API infrastructure ────────────────────────────────────────────────────
builder.Services.AddControllers();

// OpenAPI document generation — used by Scalar in Development only.
builder.Services.AddOpenApi();

// Bind the Seed section so it could be injected via IOptions<SeedOptions>
// in other services if needed in the future.
builder.Services.Configure<SeedOptions>(config.GetSection("Seed"));

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
	// NOT update an existing admin's password — use PUT /api/auth/password for that.
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
			Role = "GlobalAdmin",
			TenancyId = null
		};
		await userManager.CreateAsync(admin, adminPassword);
	}
}

app.Run();

// ── Test entry point ──────────────────────────────────────────────────────────
// The partial declaration makes Program visible to the test project so that
// WebApplicationFactory<Program> can locate and boot the application.
public partial class Program { }
