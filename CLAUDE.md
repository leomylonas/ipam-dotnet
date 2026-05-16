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
| API Docs | OpenAPI via Scalar |
| Default DB | SQLite |
| Alt DB | MySQL / MariaDB (Pomelo), PostgreSQL (Npgsql) |
| Migrations | Separate migration sets per provider |

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

### Auth

| Method | Route | Access | Description |
|---|---|---|---|
| `PUT` | `/api/auth/password` | Own user | Change own password |

### Tenancies

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/tenancies` | GlobalAdmin | List all tenancies |
| `POST` | `/api/tenancies` | GlobalAdmin | Create a tenancy (and initial TenantAdmin user) |
| `DELETE` | `/api/tenancies/{id}` | GlobalAdmin | Delete a tenancy and all associated data |

When creating a tenancy, the request body must include initial TenantAdmin credentials (username + password).

### Users

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/users` | GlobalAdmin: all users; TenantAdmin: own tenancy only | List users |
| `POST` | `/api/users` | GlobalAdmin: any tenancy + any role; TenantAdmin: TenantUser only in own tenancy | Create user |
| `DELETE` | `/api/users/{id}` | GlobalAdmin: any; TenantAdmin: own tenancy only | Delete user |
| `PUT` | `/api/users/{id}/password` | GlobalAdmin: any; TenantAdmin: own tenancy; User: own only | Change password |

### Shared Subnets

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/shared` | All authenticated | List shared subnets accessible to caller's tenancy |
| `POST` | `/api/subnets/shared` | GlobalAdmin | Create a shared subnet |
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
- Implement as a custom `AuthenticationHandler` deriving from `AuthenticationHandler<AuthenticationSchemeOptions>`
- Return `401` with `WWW-Authenticate: Basic` on failure

---

## Database Provider Selection

In `Program.cs`, read `Database:Provider` from config and switch:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var provider = config["Database:Provider"];
    var connStr = config["Database:ConnectionString"];
    switch (provider)
    {
        case "sqlite":
            options.UseSqlite(connStr, x => x.MigrationsAssembly("IpamService.Migrations.SQLite"));
            break;
        case "mysql":
            options.UseMySql(connStr, ServerVersion.AutoDetect(connStr),
                x => x.MigrationsAssembly("IpamService.Migrations.MySql"));
            break;
        case "postgres":
            options.UseNpgsql(connStr,
                x => x.MigrationsAssembly("IpamService.Migrations.Postgres"));
            break;
        default:
            throw new InvalidOperationException($"Unknown database provider: {provider}");
    }
});
```

Migrations are kept in separate folders per provider to avoid conflicts.

---

## Project Structure

```
IpamService/
├── src/
│   └── IpamService/
│       ├── Auth/
│       │   └── BasicAuthHandler.cs
│       ├── Config/
│       │   └── IpamOptions.cs
│       ├── Controllers/
│       │   ├── AuthController.cs
│       │   ├── TenanciesController.cs
│       │   ├── UsersController.cs
│       │   ├── SharedSubnetsController.cs
│       │   ├── PrivateSubnetsController.cs
│       │   ├── ExclusionsController.cs
│       │   ├── AllocationsController.cs
│       │   ├── TagsController.cs
│       │   ├── StatsController.cs
│       │   └── AuditController.cs
│       ├── Data/
│       │   ├── AppDbContext.cs
│       │   └── Migrations/
│       │       ├── SQLite/
│       │       ├── MySql/
│       │       └── Postgres/
│       ├── Models/
│       │   ├── Tenancy.cs
│       │   ├── ApplicationUser.cs
│       │   ├── Subnet.cs
│       │   ├── SubnetTenancyAccess.cs
│       │   ├── Exclusion.cs
│       │   ├── Allocation.cs
│       │   ├── AllocationTag.cs
│       │   ├── AuditLog.cs
│       │   └── DTOs/
│       │       ├── TenancyDtos.cs
│       │       ├── UserDtos.cs
│       │       ├── SubnetDtos.cs
│       │       ├── ExclusionDtos.cs
│       │       ├── AllocationDtos.cs
│       │       ├── TagDtos.cs
│       │       ├── StatsDtos.cs
│       │       └── AuditDtos.cs
│       ├── Services/
│       │   ├── IpAllocationService.cs
│       │   ├── SubnetValidationService.cs
│       │   └── AuditService.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Program.cs
├── tests/
│   └── IpamService.Tests/
│       ├── Unit/
│       │   ├── Services/
│       │   │   ├── IpAllocationServiceTests.cs
│       │   │   └── SubnetValidationServiceTests.cs
│       │   └── Auth/
│       │       └── BasicAuthHandlerTests.cs
│       ├── Integration/
│       │   └── Controllers/
│       │       ├── AuthControllerTests.cs
│       │       ├── TenanciesControllerTests.cs
│       │       ├── UsersControllerTests.cs
│       │       ├── SharedSubnetsControllerTests.cs
│       │       ├── PrivateSubnetsControllerTests.cs
│       │       ├── ExclusionsControllerTests.cs
│       │       ├── AllocationsControllerTests.cs
│       │       ├── TagsControllerTests.cs
│       │       ├── StatsControllerTests.cs
│       │       └── AuditControllerTests.cs
│       ├── System/
│       │   └── Scenarios/
│       │       ├── TenancyLifecycleTests.cs
│       │       ├── BulkAllocationTests.cs
│       │       ├── SharedSubnetAccessTests.cs
│       │       └── TagFilteringTests.cs
│       ├── Helpers/
│       │   ├── TestWebApplicationFactory.cs
│       │   ├── DatabaseFixture.cs
│       │   └── AuthHelper.cs
│       ├── appsettings.Test.json
│       └── IpamService.Tests.csproj
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

Single test project (`IpamService.Tests`) with three folder-based categories.

### Test Configuration

`appsettings.Test.json` controls the provider for all tests:

```json
{
  "TestDatabase": {
    "Provider": "sqlite",
    "ConnectionString": "Data Source=:memory:"
  },
  "Seed": {
    "AdminUsername": "admin",
    "AdminPassword": "Test1234!"
  }
}
```

To run against MySQL or PostgreSQL locally (e.g. via a Docker service container), change `Provider` and `ConnectionString`. No code changes required.

### Unit Tests
- Pure logic, no I/O, no EF, no HTTP
- `IpAllocationServiceTests` — next available IP selection, bulk consecutive logic, exclusion set handling, gap detection, 409 on no contiguous block
- `SubnetValidationServiceTests` — CIDR parsing, RFC1918 range checks, overlap detection
- `BasicAuthHandlerTests` — Base64 decode, missing header, malformed header, wrong credentials shape

### Integration Tests
- `WebApplicationFactory<Program>` + real SQLite database per test class
- Each controller has its own test class
- Tests cover: happy path, auth failures (401), permission boundary violations (403), not found (404), conflict (409)
- Identity and EF fully wired

### System Tests
- Full end-to-end scenario flows across multiple controllers using `WebApplicationFactory`
- `TenancyLifecycleTests` — create tenancy → create TenantAdmin → create TenantUser → allocate IPs → release → verify audit log
- `BulkAllocationTests` — successful bulk, verify BulkId grouping, individual release, failure case with no contiguous block
- `SharedSubnetAccessTests` — create shared subnet → restrict to tenancy → verify other tenancy cannot allocate → grant access → verify can allocate
- `TagFilteringTests` — allocate IPs → tag them → filter by tag key/value → verify correct results returned

### Test Helpers
- `TestWebApplicationFactory` — configures test provider, seeds GlobalAdmin, provides HTTP client with Basic Auth header builder
- `DatabaseFixture` — manages per-test database creation and teardown
- `AuthHelper` — builds `Authorization: Basic` headers for different roles

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
- **Steps:**
  1. Checkout
  2. Setup .NET 10
  3. `dotnet restore`
  4. `dotnet build --no-restore`
  5. `dotnet test --no-build`
  6. Docker Buildx setup
  7. Login to GHCR (`ghcr.io`) using `GITHUB_TOKEN`
  8. Build and push Docker image tagged as:
     - `ghcr.io/<owner>/<repo>:<git-tag>` (e.g. `v1.2.3`)
     - `ghcr.io/<owner>/<repo>:latest`

---

## Implementation Notes

- All timestamps stored and returned as UTC
- All IDs are `Guid`
- Return `404` when a resource is not found, `403` when access is denied to a known resource
- Allocation and release must always write an audit record — use a transaction to ensure atomicity
- `BulkId` on allocations is a `Guid` shared across all IPs in one bulk request; each IP is its own `Allocation` row
- Tag keys must be unique per allocation; a `PUT` to `/tags` is a full replace (delete all + insert)
- RFC1918 ranges: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- Subnet overlap detection should check new CIDR against all existing subnets in the same scope (same tenancy for private, global for shared)
- Use `IPNetwork` / `IPAddress` from `System.Net` for all IP arithmetic — no third-party IP libraries needed
- Register `IpAllocationService` and `SubnetValidationService` as scoped services
- Register `AuditService` as scoped, inject into controllers that need to write audit entries
- Scalar UI available at `/scalar` in Development environment only