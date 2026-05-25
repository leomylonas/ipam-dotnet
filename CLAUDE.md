# IPAM Service — Implementation Guide

This file defines the complete specification for the IPAM Service project. Use it as the authoritative reference during implementation.

---

## Code Style

### Backend (.NET)
- **Indentation:** Hard tabs (`\t`), never spaces.
- **Braces:** Always use braces on every block — `if`, `else`, `for`, `foreach`, `while`, `using`, etc. — even single-line early returns. No brace-less single-liners.
- **Comments:** Write extensive comments throughout.
  - Every type, method, and property must have XML doc comments (`///`) with `<summary>`, `<param>`, and `<returns>` where applicable.
  - Add verbose inline `//` comments inside method bodies explaining the logic and reasoning behind each step.
  - This overrides any default "no comments" behaviour.

### Frontend (TypeScript / React)
- **Indentation:** Hard tabs (`\t`), never spaces.
- **Comments:** Minimal — only for non-obvious behaviour. No JSDoc, no multi-line comment blocks.
- **Naming conventions:**
  - Components, pages, and types: PascalCase.
  - Hooks, functions, variables: camelCase.
  - Directories: PascalCase for `Components/`, `Pages/`, `Services/`, `Hooks/`, `Stores/`, `Utils/`, `Styles/`.
  - CSS Modules: camelCase class names.
- **Forms:** React Hook Form + `@hookform/resolvers/zod` v5 + Zod v4 for all forms. Use `zodResolver` with `useForm`. Prefer `handleSubmit(async (data) => { ... })` and `mutation.mutateAsync`.
- **Each service file** exports a class (`FooService`), a Zod schema per response shape, inferred TypeScript types, and a `useFooService()` hook that returns a `useMemo`-memoised instance.
- **Each hook file** exports individual named hooks (e.g. `useFooQuery`, `useFooMutation`) plus a composite `useFoo()` hook returning all related hooks as an object, for use when a component needs multiple operations.

---

## Project Overview

A generic, multi-tenant IP Address Management (IPAM) system with a .NET 10 REST API backend and a React SPA frontend. It supports multiple isolated tenancies, each with their own private subnets and users, alongside globally shared subnets managed by a GlobalAdmin. The backend supports both HTTP Basic Auth (for API consumers) and cookie-based auth (for the React UI).

---

## Stack

### Backend

| Concern | Technology |
|---|---|
| Framework | .NET 10 / ASP.NET Core Web API |
| ORM | Entity Framework Core 10 |
| Identity | ASP.NET Identity |
| Auth | Stateless HTTP Basic Auth + cookie auth (for React UI) |
| Logging | Serilog (async console sink, configured via `appsettings.json`) |
| API Docs | OpenAPI via Scalar (Development only, at `/scalar`) |
| Default DB | SQLite |
| Alt DB | MySQL (Oracle `MySql.EntityFrameworkCore`), PostgreSQL (Npgsql) |
| Migrations | Separate migration folders per provider, all in the main assembly |

### Frontend

| Concern | Technology |
|---|---|
| Framework | React 18 + TypeScript + Vite |
| UI Library | Carbon Design System (`@carbon/react` v1, `@carbon/icons-react`) |
| Routing | TanStack React Router v1 |
| Data fetching | TanStack React Query v5 |
| Forms | React Hook Form v7 + `@hookform/resolvers` v5 + Zod v4 |
| HTTP client | `@leomylonas/json-fetch-client` |
| State | `react-granular-store` (auth store only) |
| CSS | CSS Modules + Sass |
| Package manager | pnpm |
| Tests | Vitest + Testing Library (unit/component), Playwright (E2E) |

---

## Configuration (appsettings.json)

All configuration is file-driven. No runtime admin UI for config.

| Key | Type | Description |
|---|---|---|
| `Database:Provider` | `string` | `sqlite`, `mysql`, or `postgres` |
| `Database:ConnectionString` | `string` | Provider-appropriate connection string |
| `Seed:AdminUsername` | `string` | Username for the bootstrapped GlobalAdmin user |
| `Seed:AdminPassword` | `string` | Password for the bootstrapped GlobalAdmin user |
| `Dashboard:ExhaustionThresholdPercent` | `double` | Utilisation % at or above which a subnet appears in exhaustion alerts. Default `80.0` |
| `Ui:Enabled` | `bool` | When `true` (default), serves the React SPA from `wwwroot/` and falls back to `index.html`. When `false`, API-only mode. |

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

Controls which tenancies may access a shared subnet. A tenancy must have an explicit grant row to allocate from the subnet. If no rows exist for a shared subnet, it is inaccessible to all tenancies.

### Exclusion
```
Id            Guid
SubnetId      Guid (FK -> Subnet)
Start         string (IP address)
End           string (IP address, same as Start for single IP)
Description   string
```

Exclusions apply to both shared and private subnets. Single IPs use Start == End. Exclusion ranges must not include the network or broadcast address of the subnet.

### Allocation
```
Id            Guid
IpAddress     string
UserId        string (FK -> ApplicationUser)
SubnetId      Guid (FK -> Subnet)
Description   string
AllocatedAt   DateTime (UTC)
BulkId        Guid? (groups IPs from a single bulk request, nullable)
```

Tenancy is not stored directly on `Allocation` — it is derived by joining with the `Subnet` (via `SubnetId`). This avoids redundancy and allows GlobalAdmin allocations where the effective tenancy is the subnet owner's tenancy.

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

All endpoints require auth (Basic or cookie) except `/health`.

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
| `PUT` | `/api/users/{id}` | GlobalAdmin: any; TenantAdmin: own tenancy; TenantUser: own ID (password only) | Update username, role, tenancyId, and optionally password. All fields except `password` are required. TenantUser callers may only supply `password` for their own ID. |

### Shared Subnets

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/shared` | All authenticated | List shared subnets accessible to caller's tenancy |
| `POST` | `/api/subnets/shared` | GlobalAdmin | Create a shared subnet |
| `PUT` | `/api/subnets/shared/{id}` | GlobalAdmin | Update shared subnet name and description |
| `DELETE` | `/api/subnets/shared/{id}` | GlobalAdmin | Delete a shared subnet |
| `GET` | `/api/subnets/shared/{id}/access` | GlobalAdmin | List tenancies with explicit access grants to a shared subnet |
| `POST` | `/api/subnets/shared/{id}/access` | GlobalAdmin | Restrict subnet to a specific tenancy |
| `DELETE` | `/api/subnets/shared/{id}/access/{tenancyId}` | GlobalAdmin | Remove tenancy restriction |

### Private Subnets

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/tenancies/{id}/subnets` | TenantAdmin of that tenancy (or GlobalAdmin) | List private subnets |
| `POST` | `/api/tenancies/{id}/subnets` | TenantAdmin of that tenancy (or GlobalAdmin) | Create private subnet (RFC1918 validated) |
| `PUT` | `/api/tenancies/{id}/subnets/{subnetId}` | TenantAdmin of that tenancy (or GlobalAdmin) | Update private subnet name and description |
| `DELETE` | `/api/tenancies/{id}/subnets/{subnetId}` | TenantAdmin of that tenancy (or GlobalAdmin) | Delete private subnet |

### Exclusions

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/{subnetId}/exclusions` | GlobalAdmin: any; TenantAdmin: own subnets | List exclusions |
| `POST` | `/api/subnets/{subnetId}/exclusions` | GlobalAdmin: shared subnets; TenantAdmin: own private subnets | Add exclusion |
| `PUT` | `/api/subnets/{subnetId}/exclusions/{id}` | GlobalAdmin: shared subnets; TenantAdmin: own private subnets | Update exclusion description |
| `DELETE` | `/api/subnets/{subnetId}/exclusions/{id}` | GlobalAdmin: shared subnets; TenantAdmin: own private subnets | Remove exclusion |

### Allocations

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/allocations` | GlobalAdmin: all; TenantAdmin/User: own tenancy | List allocations. Filterable by `?tagKey=&tagValue=` |
| `POST` | `/api/allocations` | All authenticated roles | Request next available IP from a specified subnet |
| `POST` | `/api/allocations/bulk` | All authenticated roles | Request N consecutive IPs from a subnet. Returns 409 if no contiguous block exists |
| `GET` | `/api/subnets/{subnetId}/check/{ip}` | TenantAdmin/User (accessible subnets only) | Check if a specific IP is available |
| `DELETE` | `/api/allocations/{id}` | GlobalAdmin: any; TenantAdmin: own tenancy; User: own only | Release an allocation |

Bulk allocations share a `BulkId` but are individually releasable, each with their own audit record.

### Tags

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/allocations/{id}/tags` | GlobalAdmin: any; TenantAdmin/User: own tenancy | List tags on an allocation |
| `PUT` | `/api/allocations/{id}/tags` | GlobalAdmin: any; TenantAdmin/User: own tenancy | Full replace of all tags (key-value map in body). Returns `200 OK` with the saved tag list. |
| `DELETE` | `/api/allocations/{id}/tags/{key}` | GlobalAdmin: any; TenantAdmin/User: own tenancy | Delete a single tag by key |

### Stats

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/subnets/{subnetId}/stats` | GlobalAdmin: any; TenantAdmin/User: accessible subnets | Returns total IPs, allocated count, free count, excluded count |

### Audit

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/api/audit` | GlobalAdmin: all; TenantAdmin: own tenancy | List audit log entries, newest first |

### Auth (UI session management)

| Method | Route | Access | Description |
|---|---|---|---|
| `POST` | `/auth/login` | Public | Accepts `{ username, password }` JSON body; issues encrypted cookie on success |
| `POST` | `/auth/logout` | Authenticated | Clears the auth cookie |
| `GET` | `/auth/me` | Authenticated | Returns `{ id, username, role, tenancyId }` for the current caller |

### Dashboard

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/dashboard/global` | GlobalAdmin | System-wide stats, exhaustion alerts, and 10 recent audit entries |
| `GET` | `/dashboard/tenant` | TenantAdmin | Tenancy-scoped stats, exhaustion alerts, and 10 recent audit entries |
| `GET` | `/dashboard/user` | TenantUser | Accessible subnets with free IP counts and recent allocations |

Dashboard audit entry DTOs include `userId` (string) and `tenancyId` (Guid?, global only) alongside the human-readable `performedBy` / `tenancyName` display fields.

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

### Exclusion Validation
- Both start and end IPs must be valid IPv4 addresses
- The range must fall within the subnet's CIDR
- Start must be ≤ end (computed via `IpAllocationService.IpToUint`)
- The range must not include the network address or broadcast address of the subnet

### Subnet Utilisation
Utilisation is calculated as `(allocatedCount + excludedCount) / totalIps`. This reflects the true "unavailable" fraction — excluded IPs count as consumed capacity even if not allocated. Guard against division by zero when `totalIps == 0`.

---

## Authentication

Two schemes are accepted on all protected endpoints. Both are fully stateless from the server's perspective — no server-side session store is used.

### Basic Auth (API consumers)
- Every request may include an `Authorization: Basic <base64(user:pass)>` header
- Credentials are validated against ASP.NET Identity on every request
- Implemented as a custom `AuthenticationHandler` in `Auth/BasicAuthHandler.cs`
- Returns `401` with `WWW-Authenticate: Basic` on failure

### Cookie Auth (React UI)
- `POST /auth/login` — accepts `{ username, password }` JSON body, issues an encrypted ASP.NET Core cookie on success
- `POST /auth/logout` — clears the cookie
- `GET /auth/me` — returns `{ id, username, role, tenancyId }` for the current session; used by the UI on page load to restore routing state
- Cookie type: ASP.NET Core Data Protection encrypted cookie (stateless — no session store)
- Cookie properties: `HttpOnly`, `SameSite=Strict`, 24-hour sliding expiration
- Returns `401` (no redirect) on auth failure so the UI handles errors programmatically

### Scheme routing
A `PolicyScheme` named `"Combined"` is the default authenticate scheme. It forwards to `"Basic"` when an `Authorization` header is present, and to `"Cookie"` otherwise, so both schemes work transparently on every `[Authorize]` endpoint without extra annotation.

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
| `BadValueException` | 400 | Format/parse errors (invalid CIDR, invalid IP, network/broadcast address). Lives in `ServiceExceptions.cs`. |
| `IdentityOperationException` | 400 | Identity error descriptions in `errors` extension array |
| `NoAvailableIpException` | 409 | No free IPs remain in the subnet |
| `NoContiguousBlockException` | 409 | No run of N consecutive free IPs exists |

`BadValueException` is used for format/parse failures not tied to a named field (e.g., unparseable CIDR, invalid IP address string, exclusion hitting network/broadcast). It is distinct from `System.ComponentModel.DataAnnotations.ValidationException` — do not confuse the two.

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
| `SubnetService` | Shared and private subnet CRUD, tenancy access grants, `ListAccessAsync` |
| `SubnetValidationService` | CIDR parsing, RFC1918 checks, overlap detection |
| `ExclusionService` | Exclusion CRUD with subnet-access enforcement and network/broadcast validation |
| `IpAllocationService` | Single and bulk IP allocation, release, IP availability check. Also defines `IpToUint` (public static helper) used by `ExclusionService`. |
| `TagService` | Tag list, full replace (returns `List<TagResponse>`), single delete |
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
├── backend/
│   └── src/
│       ├── Auth/
│       │   └── BasicAuthHandler.cs
│       ├── Config/
│       │   └── IpamOptions.cs          # Seed config binding
│       ├── Controllers/
│       │   ├── IpamControllerBase.cs   # GetCaller() + ExecuteAsync() + exception mapping
│       │   ├── TenanciesController.cs
│       │   ├── UsersController.cs
│       │   ├── SharedSubnetsController.cs
│       │   ├── PrivateSubnetsController.cs
│       │   ├── ExclusionsController.cs
│       │   ├── AllocationsController.cs
│       │   ├── TagsController.cs
│       │   ├── StatsController.cs
│       │   ├── AuditController.cs
│       │   ├── AuthController.cs       # /auth/login, /auth/logout, /auth/me
│       │   └── DashboardController.cs
│       ├── Data/
│       │   ├── AppDbContext.cs
│       │   ├── ProviderDbContexts.cs   # SqliteAppDbContext / MySqlAppDbContext / PostgresAppDbContext
│       │   ├── DesignTimeDbContextFactories.cs
│       │   └── Migrations/
│       │       ├── SQLite/
│       │       ├── MySQL/
│       │       └── Postgres/
│       ├── Models/
│       │   ├── Roles.cs                # const string GlobalAdmin / TenantAdmin / TenantUser / TenantMembers
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
│       │       ├── AuditDtos.cs
│       │       └── DashboardDtos.cs
│       ├── Services/
│       │   ├── CallerContext.cs        # record passed from controller to service
│       │   ├── ServiceExceptions.cs   # NotFoundException / ForbiddenException / ConflictException / BadValueException / IdentityOperationException
│       │   ├── AuditService.cs
│       │   ├── IpAllocationService.cs # also defines NoAvailableIpException / NoContiguousBlockException / IpToUint
│       │   ├── SubnetValidationService.cs
│       │   ├── TenancyService.cs
│       │   ├── UserService.cs
│       │   ├── SubnetService.cs
│       │   ├── ExclusionService.cs
│       │   ├── TagService.cs
│       │   ├── StatsService.cs
│       │   └── DashboardService.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Program.cs
├── frontend/
│   └── src/
│       ├── Components/
│       │   ├── AppShell.tsx / .module.scss      # Layout shell (header + side nav + outlet)
│       │   ├── ThemeProvider.tsx                # Carbon g100/white theme toggling
│       │   ├── Allocations/
│       │   │   ├── AllocationList.tsx / .module.scss  # Smart list for /allocations; owns filter toolbar + all modals
│       │   │   ├── AllocationSection.tsx / .module.scss  # Smart section used by subnet detail pages
│       │   │   ├── AllocateModal.tsx            # Single-IP allocation form
│       │   │   ├── BulkAllocateModal.tsx        # Bulk allocation form
│       │   │   └── TagsModal.tsx / .module.scss # View/edit tags for an allocation
│       │   ├── Audit/
│       │   │   └── AuditList.tsx               # Smart list for /audit; fetches users+subnets for name resolution
│       │   ├── CopyableId/
│       │   │   ├── CopyableId.tsx               # Truncated ID + Carbon Tooltip + copy button
│       │   │   └── CopyableId.module.scss
│       │   ├── Dashboard/
│       │   │   ├── GlobalAdminDashboard.tsx     # GlobalAdmin view: system metrics, exhaustion alerts, audit
│       │   │   ├── TenantAdminDashboard.tsx / .module.scss  # TenantAdmin view: tenancy metrics, exhaustion alerts, audit
│       │   │   └── TenantUserDashboard.tsx      # TenantUser view: accessible subnets + recent allocations
│       │   ├── DataTable/
│       │   │   ├── IpamDataTable.tsx            # Generic Carbon table (columns, rowActions, onRowClick, toolbarContent, onSearchChange)
│       │   │   └── IpamDataTable.module.scss
│       │   ├── Exclusions/
│       │   │   ├── ExclusionSection.tsx / .module.scss  # Smart section; shared by SubnetDetailPage + SharedSubnetDetailPage
│       │   │   ├── CreateExclusionModal.tsx
│       │   │   └── EditExclusionModal.tsx
│       │   ├── Feedback/
│       │   │   ├── ErrorBanner.tsx              # Renders FetchClientError as Carbon InlineNotification
│       │   │   └── ErrorBanner.module.scss
│       │   ├── Metric/
│       │   │   ├── MetricCard.tsx               # Stat card used on dashboard/detail pages
│       │   │   └── MetricCard.module.scss
│       │   ├── Modal/
│       │   │   └── ConfirmModal.tsx             # Reusable danger-confirm modal (only generic modal remaining here)
│       │   ├── Navigation/
│       │   │   ├── AppHeader.tsx / .test.tsx    # Carbon Header with theme switcher
│       │   │   ├── AppSideNav.tsx / .module.scss / .test.tsx
│       │   │   ├── NavItem.tsx
│       │   │   └── ThemeSwitcher.tsx / .module.scss / .test.tsx
│       │   ├── PageHeader/
│       │   │   ├── PageHeader.tsx               # Page title + description + optional "Add" button
│       │   │   └── PageHeader.module.scss
│       │   ├── SharedSubnets/
│       │   │   ├── SharedSubnetList.tsx         # Smart list; navigates to /shared-subnets/$subnetId on row click
│       │   │   ├── AccessSection.tsx / .module.scss  # Smart section for tenancy access grants (GlobalAdmin only)
│       │   │   ├── CreateSharedSubnetModal.tsx
│       │   │   ├── EditSharedSubnetModal.tsx
│       │   │   └── GrantAccessModal.tsx
│       │   ├── Subnets/
│       │   │   ├── SubnetList.tsx               # Smart list; navigates to /subnets/$subnetId on row click
│       │   │   ├── SubnetMetrics.tsx            # Reusable metrics grid for subnet detail pages (total/allocated/free/excluded/utilisation)
│       │   │   ├── CreateSubnetModal.tsx
│       │   │   └── EditSubnetModal.tsx
│       │   ├── Tenancies/
│       │   │   ├── TenancyList.tsx              # Smart list; owns edit/delete modal state
│       │   │   ├── CreateTenancyModal.tsx
│       │   │   └── EditTenancyModal.tsx
│       │   └── Users/
│       │       ├── UserList.tsx                 # Smart list; reads authStore directly for caller context
│       │       ├── CreateUserModal.tsx
│       │       └── EditUserModal.tsx
│       ├── Hooks/
│       │   ├── useAllocations.ts
│       │   ├── useAudit.ts
│       │   ├── useDashboard.ts
│       │   ├── useExclusions.ts
│       │   ├── useModal.tsx             # Generic modal open/close hook: useModal(renderFn) → { isOpen, open, close, modal }
│       │   ├── useStats.ts
│       │   ├── useSubnets.ts
│       │   ├── useTags.ts
│       │   ├── useTenancies.ts
│       │   └── useUsers.ts
│       ├── Pages/
│       │   ├── Page.module.scss                 # Shared .page class (padding, max-width)
│       │   ├── AllocationsPage.tsx / .module.scss
│       │   ├── AuditPage.tsx / .module.scss
│       │   ├── DashboardPage.tsx / .module.scss / .test.tsx  # Thin orchestrator — renders role-appropriate Dashboard component
│       │   ├── LoginPage.tsx / .module.scss / .test.tsx
│       │   ├── NotFoundPage.tsx / .module.scss / .test.tsx
│       │   ├── SharedSubnetsPage.tsx            # Thin orchestrator — no .module.scss
│       │   ├── SharedSubnetDetailPage.tsx       # Back button + SubnetMetrics + AccessSection + ExclusionSection + AllocationSection
│       │   ├── SubnetDetailPage.tsx             # Back button + SubnetMetrics + ExclusionSection + AllocationSection
│       │   ├── SubnetsPage.tsx                  # Thin orchestrator — no .module.scss
│       │   ├── TenanciesPage.tsx                # Thin orchestrator — no .module.scss
│       │   └── UsersPage.tsx                    # Thin orchestrator — no .module.scss
│       ├── Services/
│       │   ├── AllocationsService.ts
│       │   ├── AuditService.ts
│       │   ├── AuthService.ts
│       │   ├── DashboardService.ts
│       │   ├── ExclusionsService.ts
│       │   ├── StatsService.ts
│       │   ├── SubnetsService.ts
│       │   ├── TagsService.ts
│       │   ├── TenanciesService.ts
│       │   └── UsersService.ts
│       ├── Stores/
│       │   ├── AuthStore.ts / .test.ts          # react-granular-store singleton; holds AuthResponse | null
│       ├── Styles/
│       │   ├── Main.scss                        # Carbon theme import + global resets
│       │   └── _Utilities.scss
│       ├── Utils/
│       │   ├── AllocationUtils.ts               # parseTagFilter(term) → AllocationFilter | undefined
│       │   ├── DashboardUtils.ts                # formatTs(iso) and utilisationIntent(pct)
│       │   └── FetchClient.ts                   # Singleton FetchClient; useFetchClient() hook
│       ├── Main.tsx                             # App entry: QueryClientProvider + RouterProvider + auth-check
│       ├── Router.tsx                           # TanStack Router route tree with per-route beforeLoad guards
│       └── ViteEnv.d.ts
├── frontend/
│   └── tests/
│       ├── e2e/
│       │   ├── helpers.ts               # Shared constants (ADMIN_USER/PASS, TENANT_ADMIN_PASS, TENANT_USER_PASS) + API helper fns
│       │   ├── global-teardown.ts       # Deletes ipam-e2e-*.db files from tmpdir after test run
│       │   ├── Login.spec.ts
│       │   ├── Dashboard.spec.ts
│       │   ├── Tenancies.spec.ts
│       │   ├── Users.spec.ts
│       │   ├── SharedSubnets.spec.ts
│       │   ├── Subnets.spec.ts
│       │   ├── Allocations.spec.ts
│       │   └── Audit.spec.ts
│       └── (unit tests live alongside source files as *.test.tsx)
├── tests/
│   ├── Unit/
│   │   ├── Services/
│   │   │   ├── IpAllocationServiceTests.cs
│   │   │   └── SubnetValidationServiceTests.cs
│   │   └── Auth/
│   │       └── BasicAuthHandlerTests.cs
│   ├── Integration/
│   │   └── Controllers/
│   │       ├── ErrorHandlingTests.cs
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
│   │       ├── TagFilteringTests.cs
│   │       └── AllocationIsolationTests.cs
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

## Frontend Architecture

### Auth flow
- On app load (`Main.tsx`), `GET /auth/me` is called before the router renders. While in flight, `authStore.isCheckingAuth` is true and the app renders nothing (prevents login flash).
- On success, the user is stored in `authStore`. On 401, the store remains unauthenticated and the router redirects to `/login`.
- `POST /auth/login` is the login form action; on success the user is stored and the router navigates to `/`.
- `POST /auth/logout` clears the server cookie; `authStore` is reset to null.

### Route guards
All auth-guarded routes use `beforeLoad` in `Router.tsx`. The guard reads from `authStore` synchronously (not React context) to avoid flashing protected page content before a redirect.

Role-based route restrictions:
- `/tenancies`, `/shared-subnets`, `/shared-subnets/$subnetId` — GlobalAdmin only
- `/users`, `/subnets`, `/subnets/$subnetId`, `/audit` — GlobalAdmin + TenantAdmin (TenantUser redirected to `/`)
- `/` (dashboard), `/allocations` — all authenticated roles

### Component organization
Components are organized by resource domain under `Components/`. Each resource directory contains its smart list/section component(s) and all related modals. Pages are thin orchestrators that import these smart components and pass minimal props.

- **Smart list components** (e.g. `TenancyList`, `SharedSubnetList`) — no required props; own their data queries, mutation state, and inline modal open/close state. Used directly from page components.
- **Smart section components** (e.g. `ExclusionSection`, `AllocationSection`, `AccessSection`) — accept a single `subnetId: string` prop; own everything else. Designed for embedding in detail pages. `ExclusionSection` is shared between `SubnetDetailPage` and `SharedSubnetDetailPage`.
- **Modal components** — live in the same directory as their owning list/section. `Components/Modal/ConfirmModal.tsx` is the only generic modal remaining in the `Modal/` directory. It accepts an optional `confirmLabel?: string` prop (defaults to `'Delete'`) so the same modal serves Release, Revoke, and Delete actions. The loading label is auto-derived by stripping a trailing `e` and appending `ing…`.

### IpamDataTable
`Components/DataTable/IpamDataTable.tsx` is the generic Carbon table wrapper. Key props:
- `columns: ColumnDef<TRow>[]` — each has `key`, `header`, `render(row) => ReactNode`
- `rows: TRow[]` — each must have a string `id` field
- `rowActions?: RowAction<TRow>[]` — renders `OverflowMenu` column when non-empty
- `onRowClick?: (row) => void` — makes rows clickable with pointer cursor
- `toolbarContent?: ReactNode` — optional content rendered inside `TableToolbarContent`
- `isLoading` — renders inline `SkeletonText` rows (5 placeholder rows) while true; does **not** replace the toolbar
- `onSearchChange?: (term: string) => void` — when provided, the parent owns filtering; IpamDataTable fires this with a 400ms debounce (immediate on clear). When omitted, IpamDataTable performs client-side string filtering.
- `emptyMessage?: string` — message shown when `rows` is empty and not loading

**Inline skeleton rows:** `isLoading` no longer triggers `DataTableSkeleton` (which would unmount the toolbar and lose search focus). Instead, `SkeletonText` cells are rendered inside the `TableBody` so the toolbar remains mounted during fetches.

**Debounce in IpamDataTable:** The `onSearchChange` callback is debounced at 400ms internally. Consumers do not need to debounce it themselves. The debounce fires immediately when the search input is cleared so there is no delay on clear.

**Carbon OverflowMenu scrollbar fix:** Carbon's `.cds--data-table-content` inner wrapper has `overflow-x: auto`, which causes a horizontal scrollbar when the OverflowMenu tooltip renders near the edge. Override with `:global(.cds--data-table-content) { overflow-x: clip; }` inside the `.container` SCSS rule.

**Carbon OverflowMenu `iconDescription`:** The `OverflowMenu` component requires `iconDescription="Open menu"` explicitly. Without it, the default is `"Options"`, and Playwright/accessibility queries using `getByRole('button', { name: 'Open menu' })` will fail.

**Toolbar layout:** Carbon's `TableToolbarContent` has a fixed `block-size: 3rem` that cannot accommodate tall inputs or multiple rows. For any toolbar with more than a simple search, render the toolbar as a standalone `<div>` **outside** `IpamDataTable` with `margin-bottom: 1rem`. Pass nothing to `toolbarContent`.

### CopyableId component
`Components/CopyableId/CopyableId.tsx` — used wherever a UUID is displayed to the user. Shows a truncated label (e.g. `abc12345…`) with a Carbon `Tooltip` containing the full ID as `<code>` and a ghost `Button` that copies it. The icon swaps to `CopyLink` for 1.5 seconds after a successful copy. Always call `e.stopPropagation()` in the click handler to prevent row-click handlers from firing.

Used in: `UserList` (tenancy column), `DashboardPage` (audit user/tenancy columns), `AllocationList` (subnet column), `AuditList` (user and subnet columns).

### ErrorBanner
`Components/Feedback/ErrorBanner.tsx` — takes `error: unknown` (typically `FetchClientError` from the HTTP client) and a `title` string. Renders a Carbon `InlineNotification` with the API error detail when present. Pass `mutation.error` directly.

### Forms pattern
All modals use React Hook Form with `zodResolver`. Mutations use `mutateAsync` so errors propagate to `mutation.error` (displayed via `ErrorBanner`) rather than throwing. Always call `mutation.reset()` alongside `reset()` in `handleClose` so stale errors are cleared when the modal reopens.

### Service / Hook pattern
Each `*Service.ts` exports:
- Zod schemas (`fooSchema`, `fooListSchema`, etc.)
- Inferred TypeScript types (`type Foo = z.infer<typeof fooSchema>`)
- Zod schemas for request bodies (used directly as `zodResolver` argument in forms)
- A `FooService` class with all API methods
- `useFooService()` hook returning a `useMemo`-memoised service instance

Each `use*.ts` exports individual named hooks plus a composite `useFoo()` returning all hooks as an object. Query key factories are defined as `fooKeys` const objects.

### Vite dev proxy
`vite.config.ts` proxies `/api`, `/auth`, `/dashboard`, and `/health` to the backend. The target is read from `process.env.VITE_BACKEND_URL` (fallback `http://localhost:5101`). E2E tests set this env var to `http://localhost:5201` via the Playwright `webServer` config so E2E requests hit the dedicated test backend rather than a running dev instance.

### Known gotchas
- **TanStack Router `from` paths:** Do not add `from` to `useNavigate` or `Link` unless the route is in the same tree — it causes type errors.
- **`@hookform/resolvers` v5 + Zod v4:** Use `zodResolver` from `@hookform/resolvers/zod` normally. Zod `z.coerce` works but `z.uuid()` and `.nullable()` behave slightly differently in v4 — verify schemas with the actual API response shape.
- **Tags PUT returns 200:** `PUT /api/allocations/{id}/tags` returns `200 OK` with the full tag list (not `204 No Content`). The `putJson` call must pass the response schema.
- **Dashboard DTOs include IDs:** Both `globalDashboardAuditEntrySchema` and `tenantDashboardAuditEntrySchema` include `userId: z.string()`. The global schema also includes `tenancyId: z.uuid().nullable()`. These are present alongside the human-readable `performedBy`/`tenancyName` display fields to support `CopyableId`.
- **`GET /api/subnets/shared/{id}/access`** is GlobalAdmin only. The access query hook (`useSubnetAccessQuery`) is disabled when `subnetId` is empty string. Grant/revoke mutations both invalidate `subnetKeys.access(subnetId)`.
- **Edit self — role select:** When a user edits their own profile (`user.id === callerId`), the role select must be hidden. Showing it would let a user demote/promote themselves.
- **GlobalAdmin allocation — tenancy select first:** `AllocateModal` and `BulkAllocateModal` are fully self-contained (no `subnetOptions` prop). For GlobalAdmin callers, they render a tenancy `Select` first; `useWatch` on `tenancyId` drives `usePrivateQuery(selectedTenancyId)` so the subnet list loads dynamically. The `tenancyId` field is stripped before sending the allocation request to the API.
- **Allocation has no `TenancyId`:** `allocationSchema` in `AllocationsService.ts` does not include `tenancyId`. Tenancy is derived server-side via the subnet. Do not add it back.
- **`onSearchChange` debounce owned by IpamDataTable:** Never wrap `onSearchChange` in an external debounce — IpamDataTable debounces it internally at 400ms. Wrapping it again will cause double-debouncing.
- **Carbon Tag text duplication:** Carbon's `<Tag>` renders the label text twice — once in `cds--tag__label` and again in an accessibility tooltip `<span>`. `getByText('someTag')` will match both elements and trigger Playwright strict-mode errors. Use `.locator('.cds--tag')` to count tags, or `.first()` to target the first match.
- **Playwright `count()` vs `waitFor()`:** `count()` returns immediately without waiting for the DOM to settle. Use `.waitFor({ state: 'visible', timeout })` on a specific locator when you need to assert presence after an async mutation.

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
- `AllocationIsolationTests` — two tenancies, two subnets; verifies TenantUser cannot see or allocate from another tenancy's subnet; GlobalAdmin sees all allocations across tenancies

### Frontend E2E Tests (Playwright)

Run with: `cd frontend && pnpm exec playwright test`

No manual server startup is needed — Playwright starts dedicated instances automatically.

**Infrastructure:**
- Backend: `http://localhost:5201` (port never collides with the dev port 5101)
- Frontend (Vite): `http://localhost:5174` (port never collides with the dev port 5173)
- Vite proxies to the backend via `VITE_BACKEND_URL=http://localhost:5201` set in the `webServer` env
- Each run uses a fresh SQLite database: `path.join(os.tmpdir(), 'ipam-e2e-<timestamp>.db')` — eliminates data pollution between runs
- `globalTeardown` (`tests/e2e/global-teardown.ts`) deletes all `ipam-e2e-*.db` files (and WAL companions) from tmpdir after the run
- `fullyParallel: false`, `workers: 4` — spec files run in parallel across workers, but tests within each file run sequentially. This ensures each file's `beforeAll` runs exactly once, preventing concurrent seed conflicts on SQLite.
- `reuseExistingServer: false` — tests always start a clean server; they will fail if the port is already in use

**Helpers (`tests/e2e/helpers.ts`):**
- Constants: `ADMIN_USER`, `ADMIN_PASS`, `TENANT_ADMIN_PASS = 'Tadmin1234!'`, `TENANT_USER_PASS = 'Tuser1234!'`, `adminBasicAuth`
- `loginAs(page, username, password)` — navigates to `/login` and completes the form
- `uniqueName(prefix)` — returns `prefix-<timestamp>` for collision-free resource names
- API helpers (use Basic auth, called from `beforeAll`/`afterAll`): `createTenancy`, `deleteTenancy`, `createSharedSubnet`, `deleteSharedSubnet`, `createPrivateSubnet`, `createUser`

**Spec files:** `Login`, `Dashboard`, `Tenancies`, `Users`, `SharedSubnets`, `Subnets`, `Allocations`, `Audit` — each creates its own tenancy/users in `beforeAll` and deletes them in `afterAll`.

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
- Tag keys must be unique per allocation; a `PUT` to `/tags` is a full replace (delete all + insert), returns `200 OK` with saved tags
- RFC1918 ranges: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- Subnet overlap detection should check new CIDR against all existing subnets in the same scope (same tenancy for private, global for shared)
- Use `IPNetwork` / `IPAddress` from `System.Net` for all IP arithmetic — no third-party IP libraries needed
- `IpAllocationService.IpToUint` is a `public static` helper used by `ExclusionService` for range comparisons
- MySQL provider is Oracle's `MySql.EntityFrameworkCore` (method: `UseMySQL` with capital SQL), not Pomelo. No `ServerVersion` is needed
- All domain services are registered as scoped; `AuditService` is injected into domain services, not controllers
- Scalar UI is available at `/scalar` in Development environment only
- The React SPA is served from `wwwroot/` at runtime. Vite's dev-server proxy rewrites `/api`, `/auth`, and `/dashboard` to the ASP.NET Core backend during development so no CORS configuration is needed
