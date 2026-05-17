# IPAM Service

A multi-tenant IP Address Management (IPAM) REST API built with .NET 10 and ASP.NET Core.

This service provides tenancy-isolated private subnet management, globally managed shared subnets, exclusion ranges, single and bulk IP allocation, allocation tagging, and full audit logging. Authentication is stateless HTTP Basic Auth on every request (except `/health`).

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
- [CI and Release Workflows](#ci-and-release-workflows)
- [Project Layout](#project-layout)
- [Implementation Notes](#implementation-notes)

## What This Project Does

IPAM Service is designed for organizations that need centralized IP management across multiple isolated tenancies.

It supports:

- A `GlobalAdmin` that manages global entities (tenancies and shared subnets).
- Tenant-scoped administrators and users.
- Private RFC1918 subnet validation and overlap checks.
- Shared subnets with optional per-tenancy access restrictions.
- Deterministic IP allocation logic with exclusion handling.
- Transactional audit-backed allocation and release operations.

## Core Features

- Multi-tenant tenancy model with hard tenancy boundaries.
- Stateless HTTP Basic authentication integrated with ASP.NET Identity.
- Role-based authorization (`GlobalAdmin`, `TenantAdmin`, `TenantUser`).
- Shared subnet management and optional tenancy-level access control.
- Private subnet management per tenancy with RFC1918 enforcement.
- Subnet overlap detection in-scope.
- Exclusion ranges per subnet.
- Single IP allocation (first available host).
- Bulk IP allocation (requires contiguous block; returns `409` when unavailable).
- Allocation tagging with full replace semantics.
- Allocation filtering by tag key/value.
- Subnet stats endpoint (total/allocated/free/excluded).
- Audit log endpoint with tenancy scoping.
- Public `/health` endpoint with database connectivity result.
- Startup migration + GlobalAdmin bootstrap seeding.

## Architecture and Stack

| Concern | Technology |
|---|---|
| Framework | .NET 10 / ASP.NET Core Web API |
| Data access | Entity Framework Core 10 |
| Identity store and password policy | ASP.NET Identity |
| Authentication | Custom `BasicAuthHandler` (`AuthenticationHandler<AuthenticationSchemeOptions>`) |
| API schema/docs | OpenAPI + Scalar (`/scalar` in Development) |
| Database providers | SQLite, MySQL/MariaDB (Oracle `MySql.EntityFrameworkCore`), PostgreSQL (Npgsql) |
| Tests | Unit + Integration + System via `WebApplicationFactory<Program>` |

## Role and Access Model

| Role | Scope |
|---|---|
| `GlobalAdmin` | Full system access, no tenancy affiliation |
| `TenantAdmin` | Manage users/subnets/exclusions/audit within own tenancy; allocate/release IPs |
| `TenantUser` | Allocate/release within accessible subnets; manage own allocation tags |

## Configuration

Configuration is file-driven via `appsettings*.json`.

### Runtime Options

| Key (`appsettings`) | Type | Required | Example | Description |
|---|---|---|---|---|
| `Database:Provider` | `string` | Yes | `sqlite` | Database provider switch. Supported values: `sqlite`, `mysql`, `postgres`. |
| `Database:ConnectionString` | `string` | Yes | `Data Source=ipam.db` | Provider-specific connection string. |
| `Seed:AdminUsername` | `string` | Yes | `admin` | GlobalAdmin username used at startup if that user does not already exist. |
| `Seed:AdminPassword` | `string` | Yes | `Admin1234!` | GlobalAdmin bootstrap password (must satisfy Identity policy). Not used to rotate existing password. |
| `Serilog:MinimumLevel:Default` | `string` | No | `Information` | Default minimum log level. |
| `Serilog:MinimumLevel:Override:*` | `string` | No | `Warning` | Per-namespace level overrides (see [Logging](#logging) section). |
| `AllowedHosts` | `string` | No | `*` | Standard ASP.NET host filtering setting. |

### Identity Password Policy (configured in code)

| Option | Value |
|---|---|
| Minimum length | `8` |
| Requires digit | `true` |
| Requires uppercase | `true` |
| Requires lowercase | `true` |
| Requires non-alphanumeric | `false` |

### Test Configuration

Tests use `TestWebApplicationFactory`, which injects configuration directly in code and does not rely on an `appsettings.Test.json` file. Each test class gets its own isolated database:

- **SQLite** — a unique temp file per factory instance, deleted after the test class finishes.
- **MySQL / PostgreSQL** — a unique database name per factory instance within the shared Testcontainer, cleaned up when the container is destroyed.

The seed admin is suppressed during tests via a placeholder username so it does not conflict with test-controlled users.

## Logging

Logging uses [Serilog](https://serilog.net/) with an async-wrapped console sink. The sink pipeline and log context enrichment are fixed in code; only the minimum levels are configurable.

### Log levels

Levels are set in the `Serilog:MinimumLevel` configuration section. The default `appsettings.json` ships with these values:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

Valid level names (from least to most verbose): `Fatal`, `Error`, `Warning`, `Information`, `Debug`, `Verbose`.

### Overriding levels at runtime

Use environment variables with `__` as the section separator:

```bash
# Raise the default level to Debug
Serilog__MinimumLevel__Default=Debug

# Suppress all EF Core output
Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Fatal

# Enable verbose output for a specific namespace
Serilog__MinimumLevel__Override__IpamService=Debug
```

In Docker, pass these with `-e`:

```bash
docker run --rm -p 8080:8080 \
  -e Serilog__MinimumLevel__Default=Debug \
  ... \
  ipam-service
```

### Sink

The console sink is wrapped in an async sink so log writes are offloaded to a background thread and do not block request handling. This is fixed in code and cannot be changed via configuration.

## Getting Started

### Prerequisites

- .NET SDK 10.0+
- One of:
  - SQLite (default)
  - MySQL/MariaDB
  - PostgreSQL

### 1. Restore and build

```bash
dotnet restore
dotnet build
```

### 2. Configure appsettings

Default config file is `src/IpamService/appsettings.json`.

Example:

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

### 3. Run the API

```bash
dotnet run --project src/IpamService/IpamService.csproj
```

### 4. OpenAPI / Scalar UI

When `ASPNETCORE_ENVIRONMENT=Development`:

- OpenAPI document is mapped by `MapOpenApi()`.
- Scalar API reference UI is available at `/scalar`.

### 5. Authenticate requests

Use HTTP Basic Auth header:

```text
Authorization: Basic <base64(username:password)>
```

If auth is missing/invalid, API returns `401` with `WWW-Authenticate: Basic`.

## Running Tests

Tests are split into three categories:

- `Unit` — pure logic, no I/O (allocation algorithm, subnet validation, basic auth handler).
- `Integration` — controller-level tests with the full ASP.NET pipeline and a real database.
- `System` — end-to-end scenario flows across multiple controllers and roles.

### SQLite (default — no external dependencies)

SQLite tests run entirely in-process with a per-test temporary file database. No Docker or database server required.

```bash
dotnet test
```

To run only the SQLite suite explicitly (excludes the Testcontainer provider suites):

```bash
dotnet test --filter "FullyQualifiedName!~MySql&FullyQualifiedName!~Postgres"
```

### MySQL and PostgreSQL (Testcontainers — requires Docker)

The MySQL and PostgreSQL suites use [Testcontainers for .NET](https://dotnet.testcontainers.org/) to spin up an ephemeral database server automatically. Docker (or a compatible runtime such as Podman with a Docker socket) must be running on the host.

Each provider suite shares a single container for the entire test run. Every test class gets its own isolated database within that container, so tests are fully independent. Containers are started and stopped automatically — no manual setup is needed.

**Run MySQL tests only:**

```bash
dotnet test --filter "FullyQualifiedName~MySql"
```

**Run PostgreSQL tests only:**

```bash
dotnet test --filter "FullyQualifiedName~Postgres"
```

**Run all three providers together:**

```bash
dotnet test
```

> The first run for each provider will pull the database Docker image (`mysql:8.0`, `postgres:16`). Subsequent runs use the cached image and start in seconds.

### Filtering by test category

```bash
# Unit tests only (all providers)
dotnet test --filter "FullyQualifiedName~Unit"

# Integration tests only (all providers)
dotnet test --filter "FullyQualifiedName~Integration"

# System tests only (all providers)
dotnet test --filter "FullyQualifiedName~System"
```

## Migrations

Each database provider has its own set of EF Core migrations because schema syntax differs across providers. Migrations live under `src/Data/Migrations/` in provider-specific subdirectories:

```
src/Data/Migrations/
├── SQLite/      — SqliteAppDbContext
├── MySQL/       — MySqlAppDbContext
└── Postgres/    — PostgresAppDbContext
```

The application applies pending migrations automatically on startup (`db.Database.Migrate()`), so there is no manual apply step required in normal operation.

### Prerequisites

Install the EF Core CLI tool if you do not already have it:

```bash
dotnet tool install --global dotnet-ef
```

### Generating a new migration

Run the command for the provider(s) you want to update. Replace `<MigrationName>` with a descriptive name (e.g. `AddSubnetDescription`). All commands must be run from the repository root.

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

The design-time factory connects to `Server=localhost;Port=3306;Database=ipam_design;User=root;Password=pass`. Adjust `src/Data/DesignTimeDbContextFactories.cs` if your local instance differs.

**PostgreSQL**

```bash
dotnet ef migrations add <MigrationName> \
  --context PostgresAppDbContext \
  --output-dir Data/Migrations/Postgres \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj
```

The design-time factory connects to `Host=localhost;Port=5432;Database=ipam_design;Username=postgres;Password=pass`. Adjust `src/Data/DesignTimeDbContextFactories.cs` if your local instance differs.

### Applying migrations manually

Migrations are applied automatically at startup, but you can apply them manually (e.g. in CI or to check what would run) using:

```bash
# SQLite
dotnet ef database update \
  --context SqliteAppDbContext \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj

# MySQL / MariaDB
dotnet ef database update \
  --context MySqlAppDbContext \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj

# PostgreSQL
dotnet ef database update \
  --context PostgresAppDbContext \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj
```

### Removing the last migration

If you generated a migration by mistake and it has not been applied to any database, remove it with:

```bash
dotnet ef migrations remove \
  --context <ContextName> \
  --project src/IpamService.csproj \
  --startup-project src/IpamService.csproj
```

## Docker

Build image:

```bash
docker build -t ipam-service .
```

Run container:

```bash
docker run --rm -p 8080:8080 \
  -e Database__Provider=sqlite \
  -e Database__ConnectionString='Data Source=ipam.db' \
  -e Seed__AdminUsername=admin \
  -e Seed__AdminPassword='Admin1234!' \
  ipam-service
```

The Dockerfile is multi-stage (`restore` -> `build` -> `publish` -> `runtime`), runs as non-root, and exposes port `8080`.

## Implementation Notes

- All timestamps are UTC.
- IDs are GUIDs.
- Private subnets are validated against RFC1918 (`10/8`, `172.16/12`, `192.168/16`).
- Overlap checks are scope-aware:
  - Private subnet overlap is checked within the same tenancy.
  - Shared subnet overlap is checked globally among shared subnets.
- Allocation/release and audit writes are designed to be atomic from the API perspective.
- Bulk allocations share a single `BulkId` but remain individually releasable allocations.
- Tag `PUT` is full replacement (`delete existing + insert provided key/value map`).
