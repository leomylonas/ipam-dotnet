# IPAM Service — Implementation Guide

This file defines the complete specification for the IPAM Service project. Use it as the authoritative reference during implementation.

---

## Code Style

- **Indentation:** Hard tabs (`\t`), never spaces.
- **Braces:** Always use braces on every block — `if`, `else`, `for`, `foreach`, `while`, `using`, etc. — even single-line early returns. No brace-less single-liners.
- **Comments:** Write extensive comments throughout.
  - Every type, method, and property must have XML doc comments (`///`) with `<summary>`, `<param>`, and `<returns>` where applicable.
  - Add verbose inline `//` comments inside method bodies explaining the logic and reasoning behind each step.
  - This overrides any default "no comments" behaviour.

---

## Project Overview

A generic, multi-tenant IP Address Management (IPAM) REST API built on .NET 10 and ASP.NET Core. It supports multiple isolated tenancies, each with their own private subnets and users, alongside globally shared subnets managed by a GlobalAdmin. Authentication is stateless HTTP Basic Auth on every request.

---

## Stack

| Concern | Technology |
|---|---|
| Framework | .NET 10 / ASP.NET Core Web API |
| ORM | Entity Framework Core 10 |
| Identity | ASP.NET Identity |
| Auth | Stateless HTTP Basic Auth (no JWT, no cookies, no sessions) |
| Logging | Serilog (async console sink, configured via `appsettings.json`) |
| API Docs | OpenAPI via Scalar (Development only, at `/scalar`) |
| Default DB | SQLite |
| Alt DB | MySQL (Oracle `MySql.EntityFrameworkCore`), PostgreSQL (Npgsql) |
| Migrations | Separate migration folders per provider, all in the main assembly |

---

## Configuration (appsettings.json)

All configuration is file-driven. No runtime admin UI for config.

| Key | Type | Description |
|---|---|---|
| `Database:Provider` | `string` | `sqlite`, `mysql`, or `postgres` |
| `Database:ConnectionString` | `string` | Provider-appropriate connection string |
| `Seed:AdminUsername` | `string` | Username for the bootstrapped GlobalAdmin user |
| `Seed:AdminPassword` | `string` | Password for the bootstrapped GlobalAdmin user |

On startup, if the GlobalAdmin user does not exist, it is created automatically from seed config.

---

## Roles

Three roles exist in the system. A user belongs to exactly one role and exactly one tenancy (GlobalAdmin has no tenancy).

| Role | Scope |
|---|---|
| `GlobalAdmin` | Full access to everything. No tenancy affiliation. |
| `TenantAdmin` | Manage users, subnets, exclusions, and view audit within their own tenancy. Can allocate/release IPs. |
| `TenantUser` | Request and release IPs on accessible subnets. Manage own allocations and tags. |

Role name strings are defined as `const string` fields on `Models.Roles` (`Roles.GlobalAdmin`, `Roles.TenantAdmin`, `Roles.TenantUser`). A composite `Roles.TenantMembers` (`"TenantAdmin,TenantUser"`) is provided for `[Authorize(Roles = ...)]` attributes. Always use these constants — never raw string literals.

---

## Data Model

### Tenancy
```
Id            Guid
Name          string (unique)
Description   string
CreatedAt     DateTime (UTC)
```

### ApplicationUser (extends IdentityUser)
```
TenancyId     Guid? (null for GlobalAdmin)
Role          string (GlobalAdmin | TenantAdmin | TenantUser)
```

### Subnet
```
Id            Guid
Cidr          string (e.g. "192.168.1.0/24")
Name          string
Description   string
Type          enum: Shared | Private
TenancyId     Guid? (null for Shared subnets)
CreatedAt     DateTime (UTC)
```

Private subnets must be RFC1918. Shared subnets are managed by GlobalAdmin.

### SubnetTenancyAccess
```
SubnetId      Guid (FK -> Subnet)
TenancyId     Guid (FK -> Tenancy)
```

Used to restrict a shared subnet to specific tenancies. If no rows exist for a shared subnet, it is accessible to all tenancies.

### Exclusion
```
Id            Guid
SubnetId      Guid (FK -> Subnet)
Start         string (IP address)
End           string (IP address, same as Start for single IP)
Description   string
```

Exclusions apply to both shared and private subnets. Single IPs use Start == End.

### Allocation
```
Id            Guid
IpAddress     string
UserId        string (FK -> ApplicationUser)
TenancyId     Guid (FK -> Tenancy)
SubnetId      Guid (FK -> Subnet)
Description   string
AllocatedAt   DateTime (UTC)
BulkId        Guid? (groups IPs from a single bulk request, nullable)
```

### AllocationTag
```
Id            Guid
AllocationId  Guid (FK -> Allocation)
Key           string
Value         string
```

Tags are freeform key-value pairs. Keys must be unique per allocation.

### AuditLog
```
Id            Guid
UserId        string (FK -> ApplicationUser)
TenancyId     Guid? (null for GlobalAdmin actions)
Action        string (e.g. "Allocated", "Released", "BulkAllocated", "SubnetCreated", etc.)
IpAddress     string?
SubnetId      Guid?
Timestamp     DateTime (UTC)
Notes         string?
```

---

## API Endpoints

All endpoints require HTTP Basic Auth except `/health`.

### Tenancies

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/tenancies` | GlobalAdmin | List all tenancies |
| `POST` | `/api/tenancies` | GlobalAdmin | Create a tenancy (and initial TenantAdmin user) |
| `PUT` | `/api/tenancies/{id}` | GlobalAdmin | Update tenancy name and description |
| `DELETE` | `/api/tenancies/{id}` | GlobalAdmin | Delete a tenancy and all associated data |

When creating a tenancy, the request body must include initial TenantAdmin credentials (username + password).

### Users

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/users` | GlobalAdmin: all users; TenantAdmin: own tenancy only | List users |
| `POST` | `/api/users` | GlobalAdmin: any tenancy + any role; TenantAdmin: TenantUser only in own tenancy | Create user |
| `DELETE` | `/api/users/{id}` | GlobalAdmin: any; TenantAdmin: own tenancy only | Delete user |
| `PUT` | `/api/users/{id}/password` | GlobalAdmin: any; TenantAdmin: own tenancy; TenantUser: own ID only | Change password |

### Shared Subnets

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/shared` | All authenticated | List shared subnets accessible to caller's tenancy |
| `POST` | `/api/subnets/shared` | GlobalAdmin | Create a shared subnet |
| `PUT` | `/api/subnets/shared/{id}` | GlobalAdmin | Update shared subnet name and description |
| `DELETE` | `/api/subnets/shared/{id}` | GlobalAdmin | Delete a shared subnet |
| `POST` | `/api/subnets/shared/{id}/access` | GlobalAdmin | Restrict subnet to a specific tenancy |
| `DELETE` | `/api/subnets/shared/{id}/access/{tenancyId}` | GlobalAdmin | Remove tenancy restriction |

### Private Subnets

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/tenancies/{id}/subnets` | TenantAdmin of that tenancy (or GlobalAdmin) | List private subnets |
| `POST` | `/api/tenancies/{id}/subnets` | TenantAdmin of that tenancy (or GlobalAdmin) | Create private subnet (RFC1918 validated) |
| `DELETE` | `/api/tenancies/{id}/subnets/{subnetId}` | TenantAdmin of that tenancy (or GlobalAdmin) | Delete private subnet |

### Exclusions

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/{subnetId}/exclusions` | GlobalAdmin: any; TenantAdmin: own subnets | List exclusions |
| `POST` | `/api/subnets/{subnetId}/exclusions` | GlobalAdmin: shared subnets; TenantAdmin: own private subnets | Add exclusion |
| `DELETE` | `/api/subnets/{subnetId}/exclusions/{id}` | GlobalAdmin: shared subnets; TenantAdmin: own private subnets | Remove exclusion |

### Allocations

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/allocations` | GlobalAdmin: all; TenantAdmin/User: own tenancy | List allocations. Filterable by `?tagKey=&tagValue=` |
| `POST` | `/api/allocations` | TenantAdmin/User | Request next available IP from a specified subnet |
| `POST` | `/api/allocations/bulk` | TenantAdmin/User | Request N consecutive IPs from a subnet. Returns 409 if no contiguous block exists |
| `GET` | `/api/subnets/{subnetId}/check/{ip}` | TenantAdmin/User (accessible subnets only) | Check if a specific IP is available |
| `DELETE` | `/api/allocations/{id}` | GlobalAdmin: any; TenantAdmin: own tenancy; User: own only | Release an allocation |

Bulk allocations share a `BulkId` but are individually releasable, each with their own audit record.

### Tags

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/allocations/{id}/tags` | GlobalAdmin: any; TenantAdmin/User: own tenancy | List tags on an allocation |
| `PUT` | `/api/allocations/{id}/tags` | GlobalAdmin: any; TenantAdmin/User: own tenancy | Full replace of all tags (key-value map in body) |
| `DELETE` | `/api/allocations/{id}/tags/{key}` | GlobalAdmin: any; TenantAdmin/User: own tenancy | Delete a single tag by key |

### Stats

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/{subnetId}/stats` | GlobalAdmin: any; TenantAdmin/User: accessible subnets | Returns total IPs, allocated count, free count, excluded count |

### Audit

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/audit` | GlobalAdmin: all; TenantAdmin: own tenancy | List audit log entries, newest first |

### Health

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/health` | Public | Returns 200 OK with database connectivity status |

---

## Allocation Logic

### Single Allocation
1. Load target subnet and its exclusions from DB
2. Load all currently allocated IPs in that subnet
3. Build excluded set = config exclusions + allocated IPs
4. Walk subnet IP range in order, return first IP not in excluded set
5. Write Allocation + AuditLog in a single EF transaction

### Bulk Allocation
1. Same exclusion set as above
2. Walk subnet IP range looking for N consecutive IPs with no gaps in excluded set
3. If no contiguous block of size N exists, throw — return HTTP 409 with descriptive message
4. Write all N Allocations (sharing a new `BulkId`) + N individual AuditLog entries in a single transaction

### Subnet Validation
- Private subnets must fall within RFC1918 ranges: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- CIDR must be valid and parseable
- Overlapping subnets within the same tenancy should be rejected

---

## Authentication

- Every request (except `/health`) requires an `Authorization: Basic <base64(user:pass)>` header
- Credentials are validated against ASP.NET Identity on every request
- No session, no cookie, no token issuance
- Implemented as a custom `AuthenticationHandler` in `Auth/BasicAuthHandler.cs`
- Returns `401` with `WWW-Authenticate: Basic` on failure

---

## Error Handling

All error responses follow RFC 7807 Problem Details (`application/problem+json`). There are two layers:

### Typed service exceptions (`IpamControllerBase.ExecuteAsync`)
All controllers inherit `IpamControllerBase` and wrap their action bodies in `ExecuteAsync(...)`. This catches typed service exceptions and maps them to Problem Details responses:

| Exception | HTTP status | Notes |
|---|---|---|
| `NotFoundException` | 404 | `detail` included when exception carries a message |
| `ForbiddenException` | 403 | No body — access denial is never explained |
| `ConflictException` | 409 | Business-rule conflict message in `detail` |
| `ValidationException` | 400 | Validation failure message in `detail` |
| `IdentityOperationException` | 400 | Identity error descriptions in `errors` extension array |
| `NoAvailableIpException` | 409 | No free IPs remain in the subnet |
| `NoContiguousBlockException` | 409 | No run of N consecutive free IPs exists |

All typed exceptions are defined in `Services/ServiceExceptions.cs`, except `NoAvailableIpException` and `NoContiguousBlockException` which live in `Services/IpAllocationService.cs`.

### Global unhandled exceptions (`UseExceptionHandler`)
`Program.cs` registers `UseExceptionHandler` as the outermost middleware. Any exception that escapes the controller pipeline (i.e. not a typed service exception) is caught and converted to a 500 Problem Details response via `IProblemDetailsService`. `AddProblemDetails()` must be called during service registration to enable this.

---

## Database Provider Selection

Three provider-specific `AppDbContext` subclasses (`SqliteAppDbContext`, `MySqlAppDbContext`, `PostgresAppDbContext`) are defined in `Data/ProviderDbContexts.cs`. Each is a thin wrapper whose only purpose is to carry a distinct `DbContextOptions<T>` type so that EF Core's migration tooling can resolve the correct migration set.

In `Program.cs`, register the correct subclass based on configuration:

```csharp
switch (provider)
{
    case "sqlite":
        builder.Services.AddDbContext<AppDbContext, SqliteAppDbContext>(options =>
            options.UseSqlite(connStr, x => x.MigrationsAssembly("IpamService")));
        break;
    case "mysql":
        // Oracle MySql.EntityFrameworkCore — UseMySQL (capital SQL), not Pomelo's UseMySql.
        builder.Services.AddDbContext<AppDbContext, MySqlAppDbContext>(options =>
            options.UseMySQL(connStr, x => x.MigrationsAssembly("IpamService")));
        break;
    case "postgres":
        builder.Services.AddDbContext<AppDbContext, PostgresAppDbContext>(options =>
            options.UseNpgsql(connStr, x => x.MigrationsAssembly("IpamService")));
        break;
    default:
        throw new InvalidOperationException($"Unknown database provider: {provider}");
}
```

Migration folders live under `Data/Migrations/SQLite/`, `Data/Migrations/MySQL/`, and `Data/Migrations/Postgres/`, all compiled into the main `IpamService` assembly.

---

## Service Architecture

Business logic is split across domain-area services, all registered as scoped. Controllers call services and never touch `AppDbContext` directly.

| Service | Responsibility |
|---|---|
| `TenancyService` | Tenancy lifecycle (create, list, update, delete with cascade) |
| `UserService` | User CRUD and password management |
| `SubnetService` | Shared and private subnet CRUD, tenancy access grants |
| `SubnetValidationService` | CIDR parsing, RFC1918 checks, overlap detection |
| `ExclusionService` | Exclusion CRUD with subnet-access enforcement |
| `IpAllocationService` | Single and bulk IP allocation, release, IP availability check |
| `TagService` | Tag list, full replace, single delete |
| `StatsService` | Subnet utilisation stats |
| `AuditService` | Writes and queries audit log entries |

`AuditService` is injected into the domain services that write audit records; controllers do not interact with it directly.

### CallerContext
`Services/CallerContext.cs` is a `record` passed from controller to service to carry the authenticated caller's identity:

```csharp
public record CallerContext(string UserId, string Role, Guid? TenancyId)
{
    public bool IsGlobalAdmin => Role == Roles.GlobalAdmin;
    public bool IsTenantAdmin => Role == Roles.TenantAdmin;
    public bool IsTenantUser  => Role == Roles.TenantUser;
}
```

`IpamControllerBase.GetCaller()` constructs it from HTTP claims on every request.

---

## Project Structure

```
IpamService/
├── src/
│   ├── Auth/
│   │   └── BasicAuthHandler.cs
│   ├── Config/
│   │   └── IpamOptions.cs          # Seed config binding
│   ├── Controllers/
│   │   ├── IpamControllerBase.cs   # GetCaller() + ExecuteAsync() + exception mapping
│   │   ├── TenanciesController.cs
│   │   ├── UsersController.cs
│   │   ├── SharedSubnetsController.cs
│   │   ├── PrivateSubnetsController.cs
│   │   ├── ExclusionsController.cs
│   │   ├── AllocationsController.cs
│   │   ├── TagsController.cs
│   │   ├── StatsController.cs
│   │   └── AuditController.cs
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── ProviderDbContexts.cs   # SqliteAppDbContext / MySqlAppDbContext / PostgresAppDbContext
│   │   ├── DesignTimeDbContextFactories.cs
│   │   └── Migrations/
│   │       ├── SQLite/
│   │       ├── MySQL/
│   │       └── Postgres/
│   ├── Models/
│   │   ├── Roles.cs                # const string GlobalAdmin / TenantAdmin / TenantUser / TenantMembers
│   │   ├── Tenancy.cs
│   │   ├── ApplicationUser.cs
│   │   ├── Subnet.cs
│   │   ├── SubnetTenancyAccess.cs
│   │   ├── Exclusion.cs
│   │   ├── Allocation.cs
│   │   ├── AllocationTag.cs
│   │   ├── AuditLog.cs
│   │   └── DTOs/
│   │       ├── TenancyDtos.cs
│   │       ├── UserDtos.cs
│   │       ├── SubnetDtos.cs
│   │       ├── ExclusionDtos.cs
│   │       ├── AllocationDtos.cs
│   │       ├── TagDtos.cs
│   │       ├── StatsDtos.cs
│   │       └── AuditDtos.cs
│   ├── Services/
│   │   ├── CallerContext.cs        # record passed from controller to service
│   │   ├── ServiceExceptions.cs   # NotFoundException / ForbiddenException / ConflictException / ValidationException / IdentityOperationException
│   │   ├── AuditService.cs
│   │   ├── IpAllocationService.cs # also defines NoAvailableIpException / NoContiguousBlockException
│   │   ├── SubnetValidationService.cs
│   │   ├── TenancyService.cs
│   │   ├── UserService.cs
│   │   ├── SubnetService.cs
│   │   ├── ExclusionService.cs
│   │   ├── TagService.cs
│   │   └── StatsService.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── Program.cs
├── tests/
│   ├── Unit/
│   │   ├── Services/
│   │   │   ├── IpAllocationServiceTests.cs
│   │   │   └── SubnetValidationServiceTests.cs
│   │   └── Auth/
│   │       └── BasicAuthHandlerTests.cs
│   ├── Integration/
│   │   └── Controllers/
│   │       ├── ErrorHandlingTests.cs                        # Problem Details + global exception handler
│   │       ├── TenanciesControllerTests.cs
│   │       ├── UsersControllerTests.cs
│   │       ├── SharedSubnetsControllerTests.cs
│   │       ├── PrivateSubnetsAndExclusionsControllerTests.cs
│   │       └── AllocationsControllerTests.cs
│   ├── System/
│   │   └── Scenarios/
│   │       ├── TenancyLifecycleTests.cs
│   │       ├── BulkAllocationTests.cs
│   │       ├── SharedSubnetAccessTests.cs
│   │       └── TagFilteringTests.cs
│   ├── Helpers/
│   │   ├── TestWebApplicationFactory.cs
│   │   ├── MySqlTestWebApplicationFactory.cs
│   │   ├── PostgresTestWebApplicationFactory.cs
│   │   ├── MySqlContainerFixture.cs
│   │   ├── PostgresContainerFixture.cs
│   │   └── AuthHelper.cs
│   └── IpamService.Tests.csproj
├── .github/
│   └── workflows/
│       ├── ci.yml
│       └── release.yml
├── Dockerfile
├── .dockerignore
├── IpamService.sln
└── README.md
```

---

## Test Suite

Single test project (`IpamService.Tests`) with three folder-based categories. Each integration/system test class uses `IAsyncLifetime` and calls `Factory.SeedDatabaseAsync(...)` to populate its own isolated database before the tests run.

### Test Infrastructure

The test host is built with `TestWebApplicationFactory : WebApplicationFactory<Program>`, which:
- Redirects the app to a per-instance SQLite file database (unique temp path per factory instance)
- Suppresses the startup seed so the test controls all users itself
- Exposes `SeedDatabaseAsync(Func<AppDbContext, UserManager<ApplicationUser>, Task>)` for test fixtures
- Exposes `CreateAuthenticatedClient(username, password)` which builds a client with a pre-set `Authorization: Basic` header

`MySqlTestWebApplicationFactory` and `PostgresTestWebApplicationFactory` extend the base factory and redirect to Testcontainer-backed databases. `MySqlContainerFixture` and `PostgresContainerFixture` are xUnit collection fixtures that spin up a shared container for the MySQL and PostgreSQL test suites respectively.

`AuthHelper` provides a `SetBasicAuth(username, password)` extension method on `HttpClient`.

There is no `DatabaseFixture` class — isolation is achieved via unique per-instance SQLite files, not a shared fixture.

### Unit Tests
- Pure logic, no I/O, no EF, no HTTP
- `IpAllocationServiceTests` — next available IP selection, bulk consecutive logic, exclusion set handling, gap detection, 409 on no contiguous block
- `SubnetValidationServiceTests` — CIDR parsing, RFC1918 range checks, overlap detection
- `BasicAuthHandlerTests` — Base64 decode, missing header, malformed header, wrong credentials shape

### Integration Tests
- `WebApplicationFactory<Program>` + real per-instance SQLite database
- Each test class uses the abstract base + concrete subclass pattern: the base contains all test methods, concrete subclasses (`FooTests`, `FooMySqlTests`, `FooPostgresTests`) supply the factory so the same tests run against all providers
- `ErrorHandlingTests` — verifies every typed exception maps to the correct Problem Details status/content-type/fields, and that unhandled exceptions reach the global `UseExceptionHandler` and produce a 500 Problem Details response. Uses `ErrorHandlingTestWebApplicationFactory` which registers a test-only `ThrowingController` via `AddApplicationPart`

### System Tests
- Full end-to-end scenario flows across multiple controllers using `WebApplicationFactory`
- `TenancyLifecycleTests` — create tenancy → create TenantAdmin → create TenantUser → allocate IPs → release → verify audit log
- `BulkAllocationTests` — successful bulk, verify BulkId grouping, individual release, failure case with no contiguous block
- `SharedSubnetAccessTests` — create shared subnet → restrict to tenancy → verify other tenancy cannot allocate → grant access → verify can allocate
- `TagFilteringTests` — allocate IPs → tag them → filter by tag key/value → verify correct results returned

---

## Docker

Multistage, cache-optimised Dockerfile:

| Stage | Base Image | Purpose |
|---|---|---|
| `restore` | `mcr.microsoft.com/dotnet/sdk:10.0` | Copy `.sln` + `.csproj` files only, run `dotnet restore` — cached until deps change |
| `build` | restore | Copy source, run `dotnet build` |
| `publish` | build | Run `dotnet publish -c Release -o /app/publish` |
| `runtime` | `mcr.microsoft.com/dotnet/aspnet:10.0` | Copy publish output, create non-root user, expose port 8080 |

The `.dockerignore` should exclude `bin/`, `obj/`, `*.Tests/`, `.git/`, `.github/`.

---

## GitHub Actions

### `ci.yml` — Continuous Integration
- **Trigger:** push or pull_request to any branch
- **Steps:**
  1. Checkout
  2. Setup .NET 10
  3. `dotnet restore`
  4. `dotnet build --no-restore`
  5. `dotnet test --no-build` (SQLite only, no service containers needed)
  6. Upload test results as artifact

### `release.yml` — Release & Publish
- **Trigger:** push of tags matching `v*.*.*`
- **Jobs** (run in sequence — each depends on the previous):
  1. **`test`** — restore, build, run SQLite test suite (same filter as CI)
  2. **`release`** (matrix) — for each target platform (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`): `dotnet publish` with `--self-contained true -p:PublishSingleFile=true`, then archive as `.zip` (Windows) or `.tar.gz` (all others), upload archive as a workflow artifact
  3. **`publish-release`** — download all binary artifacts, create a GitHub Release with auto-generated notes and all archives as assets; then build and push the Docker image to GHCR tagged as `<version>` and `latest`
- Trimming (`PublishTrimmed`) is **disabled** — ASP.NET Core and EF Core use reflection and are not trim-compatible without significant extra annotation work

---

## Implementation Notes

- All timestamps stored and returned as UTC
- All IDs are `Guid`
- Return `404` when a resource is not found, `403` when access is denied to a known resource. Both are Problem Details responses
- Allocation and release must always write an audit record — use a transaction to ensure atomicity
- `BulkId` on allocations is a `Guid` shared across all IPs in one bulk request; each IP is its own `Allocation` row
- Tag keys must be unique per allocation; a `PUT` to `/tags` is a full replace (delete all + insert)
- RFC1918 ranges: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- Subnet overlap detection should check new CIDR against all existing subnets in the same scope (same tenancy for private, global for shared)
- Use `IPNetwork` / `IPAddress` from `System.Net` for all IP arithmetic — no third-party IP libraries needed
- MySQL provider is Oracle's `MySql.EntityFrameworkCore` (method: `UseMySQL` with capital SQL), not Pomelo. No `ServerVersion` is needed
- All domain services are registered as scoped; `AuditService` is injected into domain services, not controllers
- Scalar UI is available at `/scalar` in Development environment only
