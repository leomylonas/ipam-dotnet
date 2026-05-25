# IPAM Service

A multi-tenant IP Address Management (IPAM) system with a .NET 10 REST API backend and a React SPA frontend.

It supports multiple isolated tenancies, each with their own private subnets and users, alongside globally shared subnets managed by a GlobalAdmin. The backend supports stateless HTTP Basic Auth for API consumers and cookie-based auth for the React UI.

## Contents

- [What This Project Does](#what-this-project-does)
- [Core Features](#core-features)
- [Architecture and Stack](#architecture-and-stack)
- [Role and Access Model](#role-and-access-model)
- [Configuration](#configuration)
- [Logging](#logging)
- [Getting Started](#getting-started)
- [Running Tests](#running-tests)
- [Migrations](#migrations)
- [Docker](#docker)

## What This Project Does

IPAM Service is designed for organisations that need centralised IP management across multiple isolated tenancies.

It supports:

- A `GlobalAdmin` that manages global entities (tenancies and shared subnets).
- Tenant-scoped administrators and users.
- Private RFC1918 subnet validation and overlap checks.
- Shared subnets with optional per-tenancy access restrictions.
- Deterministic IP allocation logic with exclusion handling.
- Transactional audit-backed allocation and release operations.
- A full React SPA served by the same process, with role-aware dashboards and full CRUD for all resources.

## Core Features

- Multi-tenant tenancy model with hard tenancy boundaries.
- Stateless HTTP Basic Auth for API consumers; cookie auth for the browser UI.
- Role-based authorisation (`GlobalAdmin`, `TenantAdmin`, `TenantUser`).
- Shared subnet management with optional tenancy-level access control.
- Private subnet management per tenancy with RFC1918 enforcement.
- Subnet overlap detection within scope.
- Exclusion ranges per subnet.
- Single IP allocation (first available host).
- Bulk IP allocation (requires contiguous block; returns `409` when unavailable).
- Allocation tagging with full replace semantics.
- Allocation filtering by tag key/value.
- Subnet stats endpoint (total/allocated/free/excluded).
- Role-scoped dashboard endpoints.
- Audit log endpoint with tenancy scoping.
- Public `/health` endpoint with database connectivity result.
- Startup migration + GlobalAdmin bootstrap seeding.

## Architecture and Stack

### Backend

| Concern | Technology |
|---|---|
| Framework | .NET 10 / ASP.NET Core Web API |
| Data access | Entity Framework Core 10 |
| Identity | ASP.NET Identity |
| Authentication | HTTP Basic Auth + cookie auth (combined policy scheme) |
| API docs | OpenAPI + Scalar (`/scalar` in Development only) |
| Logging | Serilog (async console sink) |
| Database providers | SQLite, MySQL/MariaDB (Oracle `MySql.EntityFrameworkCore`), PostgreSQL (Npgsql) |
| Tests | xUnit — Unit + Integration + System via `WebApplicationFactory<Program>` |

### Frontend

| Concern | Technology |
|---|---|
| Framework | React 18 + TypeScript + Vite |
| UI library | Carbon Design System (`@carbon/react` v1) |
| Routing | TanStack React Router v1 |
| Data fetching | TanStack React Query v5 |
| Forms | React Hook Form v7 + Zod v4 |
| HTTP client | `@leomylonas/json-fetch-client` |
| Package manager | pnpm |
| Tests | Vitest (unit), Playwright (E2E) |

## Role and Access Model

| Role | Scope |
|---|---|
| `GlobalAdmin` | Full system access, no tenancy affiliation |
| `TenantAdmin` | Manage users/subnets/exclusions/audit within own tenancy; allocate/release IPs |
| `TenantUser` | Allocate/release within accessible subnets; manage own allocation tags |

## Configuration

Configuration is file-driven via `appsettings*.json`.

### Runtime Options

| Key | Type | Required | Default | Description |
|---|---|---|---|---|
| `Database:Provider` | `string` | Yes | — | `sqlite`, `mysql`, or `postgres` |
| `Database:ConnectionString` | `string` | Yes | — | Provider-specific connection string |
| `Seed:AdminUsername` | `string` | Yes | — | GlobalAdmin username bootstrapped on first startup |
| `Seed:AdminPassword` | `string` | Yes | — | GlobalAdmin bootstrap password (must satisfy Identity policy) |
| `Dashboard:ExhaustionThresholdPercent` | `double` | No | `80.0` | Utilisation % at or above which a subnet appears in exhaustion alerts |
| `Ui:Enabled` | `bool` | No | `true` | When `true`, serves the React SPA from `wwwroot/`; when `false`, API-only mode |
| `Serilog:MinimumLevel:Default` | `string` | No | `Information` | Default minimum log level |

### Identity Password Policy

| Option | Value |
|---|---|
| Minimum length | `8` |
| Requires digit | `true` |
| Requires uppercase | `true` |
| Requires lowercase | `true` |
| Requires non-alphanumeric | `false` |

## Logging

Logging uses [Serilog](https://serilog.net/) with an async-wrapped console sink.

### Overriding levels at runtime

Use environment variables with `__` as the section separator:

```bash
Serilog__MinimumLevel__Default=Debug
Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Fatal
```

## Getting Started

### Prerequisites

- .NET SDK 10.0+
- Node.js 20+ and pnpm 10+
- SQLite (default), MySQL/MariaDB, or PostgreSQL

### 1. Backend

```bash
cd backend
dotnet restore
dotnet run --project src/IpamService.csproj
```

The API starts on `http://localhost:5101` by default. The OpenAPI/Scalar UI is available at `http://localhost:5101/scalar` in Development.

### 2. Frontend (dev server)

```bash
cd frontend
pnpm install
pnpm dev
```

The dev server starts on `http://localhost:5173` and proxies `/api`, `/auth`, `/dashboard`, and `/health` to the backend automatically — no CORS configuration needed.

### 3. Configure appsettings

Edit `backend/src/appsettings.json`:

```json
{
  "Database": {
    "Provider": "sqlite",
    "ConnectionString": "Data Source=ipam.db"
  },
  "Seed": {
    "AdminUsername": "admin",
    "AdminPassword": "Admin1234!"
  }
}
```

### 4. Authenticate

**Browser UI** — log in at `/login` with the seeded admin credentials. The UI issues a session cookie automatically.

**API / curl** — use HTTP Basic Auth:

```bash
curl -u admin:Admin1234! http://localhost:5101/api/tenancies
```

## Running Tests

### Backend

Tests are split into three categories:

- **Unit** — pure logic, no I/O (allocation algorithm, subnet validation, Basic Auth handler).
- **Integration** — controller-level tests with the full ASP.NET pipeline and a real database.
- **System** — end-to-end scenario flows across multiple controllers and roles.

```bash
cd backend

# SQLite only (default — no external dependencies)
dotnet test --filter "FullyQualifiedName!~MySql&FullyQualifiedName!~Postgres"

# MySQL only (requires Docker)
dotnet test --filter "FullyQualifiedName~MySql"

# PostgreSQL only (requires Docker)
dotnet test --filter "FullyQualifiedName~Postgres"

# All providers
dotnet test
```

MySQL and PostgreSQL suites use [Testcontainers for .NET](https://dotnet.testcontainers.org/) — Docker must be running on the host. Each test class gets its own isolated database within the shared container.

### Frontend unit tests

```bash
cd frontend
pnpm test
```

### Frontend E2E tests (Playwright)

No manual server startup needed — Playwright starts dedicated backend and frontend instances automatically on separate ports (5201 / 5174) with a fresh SQLite database per run.

```bash
cd frontend
pnpm exec playwright test
```

The HTML report is written to `frontend/playwright-report/`.

## Migrations

Each provider has its own EF Core migration set. Run all commands from the `backend/` directory.

```bash
cd backend
dotnet tool install --global dotnet-ef  # if not already installed
```

### Generate a new migration

**SQLite**

```bash
dotnet ef migrations add <MigrationName> \
  --context SqliteAppDbContext \
  --output-dir Data/Migrations/SQLite \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj
```

**MySQL / MariaDB**

```bash
dotnet ef migrations add <MigrationName> \
  --context MySqlAppDbContext \
  --output-dir Data/Migrations/MySQL \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj
```

**PostgreSQL**

```bash
dotnet ef migrations add <MigrationName> \
  --context PostgresAppDbContext \
  --output-dir Data/Migrations/Postgres \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj
```

The design-time factories use local credentials defined in `src/Data/DesignTimeDbContextFactories.cs`. Adjust them if your local instance differs.

Migrations are applied automatically on startup — no manual apply step is required in normal operation.

## Docker

### Docker Compose (quickest start)

```bash
docker compose up
```

The app starts on `http://localhost:8080` with a named volume for the SQLite database. Edit `docker-compose.yml` to change the admin credentials or switch to MySQL/PostgreSQL.

### Build and run manually

```bash
# Build (from repo root)
docker build -t ipam-service .

# Run
docker run --rm -p 8080:8080 \
  -v ipam-data:/data \
  -e Database__Provider=sqlite \
  -e Database__ConnectionString='Data Source=/data/ipam.db' \
  -e Seed__AdminUsername=admin \
  -e Seed__AdminPassword='Admin1234!' \
  ipam-service
```

The Dockerfile is multi-stage: Node builds the React SPA, then .NET restores, builds, and publishes with the SPA assets copied into `wwwroot/`. The runtime image runs as a non-root user on port `8080`.

### Pre-built image (GHCR)

```bash
docker pull ghcr.io/leomylonas/dotnet-ipam:latest
```

| Tag | Description |
|---|---|
| `latest` | Most recent stable release |
| `v1.2.3` | Specific release version |

