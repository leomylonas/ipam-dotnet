# IPAM Service

A multi-tenant IP Address Management (IPAM) REST API built with .NET 10 and ASP.NET Core.

This service provides tenancy-isolated private subnet management, globally managed shared subnets, exclusion ranges, single and bulk IP allocation, allocation tagging, and full audit logging. Authentication is stateless HTTP Basic Auth on every request (except `/health`).

## Contents

- [What This Project Does](#what-this-project-does)
- [Core Features](#core-features)
- [Architecture and Stack](#architecture-and-stack)
- [Role and Access Model](#role-and-access-model)
- [Configuration](#configuration)
- [Getting Started](#getting-started)
- [Running Tests](#running-tests)
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
| Database providers | SQLite, MySQL/MariaDB (Pomelo), PostgreSQL (Npgsql) |
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
| `Logging:LogLevel:Default` | `string` | No | `Information` | Default log level for app logging. |
| `Logging:LogLevel:Microsoft.AspNetCore` | `string` | No | `Warning` | ASP.NET Core log level override. |
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

`tests/IpamService.Tests/appsettings.Test.json`:

| Key (`appsettings`) | Example | Purpose |
|---|---|---|
| `Database:Provider` | `sqlite` | Test provider selection. |
| `Database:ConnectionString` | `Data Source=:memory:` | Test database connection string. |
| `Seed:AdminUsername` | `admin` | Seed admin for tests. |
| `Seed:AdminPassword` | `Test1234!` | Seed admin password for tests. |

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

```bash
dotnet test
```

Test structure:

- `Unit`: pure logic tests (allocation, subnet validation, basic auth handler).
- `Integration`: controller-level tests with real app pipeline and database.
- `System`: end-to-end scenarios across controllers and roles.

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
