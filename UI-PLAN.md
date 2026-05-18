# IPAM UI — Full Plan

## Overview

A React-based single-page application served by the existing ASP.NET Core API container.
The UI is role-aware and communicates with the API via cookie authentication, while stateless
Basic Auth remains fully functional for direct API consumers.

---

## Technology Stack

| Concern | Library |
|---|---|
| Build | Vite + React + TypeScript 6 |
| Design system | `@carbon/react` + `@carbon/icons-react` |
| Routing | `@tanstack/react-router` (code-based) |
| Server state | `@tanstack/react-query` |
| Schema / validation | `zod` |
| HTTP | `@leomylonas/json-fetch-client` |
| Package manager | `pnpm` |
| Linter | ESLint 9 (flat config `eslint.config.ts`) |
| Git hooks | `husky` + `lint-staged` |

---

## Repository Structure

```
frontend/               ← React/Vite app (repo root)
backend/                ← existing ASP.NET Core API
```

---

## Project Structure

```
frontend/
  src/
    api/            ← FetchClient instance + per-resource API functions
    schemas/        ← Zod schemas (one file per resource)
    hooks/          ← TanStack Query hooks wrapping API functions
    router/         ← route tree + role guards
    pages/          ← route-level page components (Carbon components only)
    components/     ← composite components (Carbon components only)
  index.html
  vite.config.ts
  tsconfig.json
  package.json
  eslint.config.ts
  .prettierrc
```

---

## Authentication

- Existing Basic Auth handler remains unchanged
- New `POST /auth/login` endpoint — validates Basic Auth credentials, issues encrypted ASP.NET Core cookie
- New `POST /auth/logout` endpoint — clears cookie
- Cookie type — ASP.NET Core encrypted cookie (stateless; no session store required)
- Authorisation policy updated to accept either scheme (Basic Auth or cookie) on all endpoints
- UI `FetchClient` configured with `credentials: 'include'` (the library default)

---

## Theme

- Defaults to the user's OS `prefers-color-scheme` setting (light → Carbon White; dark → Carbon g90)
- Manual toggle persisted to `localStorage` overrides the OS preference
- Toggle rendered in the top navigation bar

---

## Route Structure

```
rootRoute
├── loginRoute              /login
└── appRoute                /  (authenticated layout; role guard in beforeLoad)
    ├── dashboardRoute      /
    ├── tenanciesRoute      /tenancies              (GlobalAdmin)
    ├── usersRoute          /users                  (GlobalAdmin, TenantAdmin)
    ├── sharedSubnetsRoute  /shared-subnets         (GlobalAdmin)
    ├── subnetsRoute        /subnets                (GlobalAdmin, TenantAdmin)
    │   └── subnetRoute     /subnets/$subnetId
    ├── allocationsRoute    /allocations            (all roles)
    └── auditRoute          /audit                  (GlobalAdmin, TenantAdmin)
```

---

## Page Functionality by Role

| Page | GlobalAdmin | TenantAdmin | TenantUser |
|---|---|---|---|
| Dashboard | System-wide stats + exhaustion alerts + recent audit | Tenancy stats + exhaustion alerts + recent audit | Accessible subnets + recent accessible allocations |
| Tenancies | List, create, update name, delete | — | — |
| Users | List, create, update (password, role), delete (all tenancies) | List, create, update (password, role), delete (own tenancy) | — |
| Shared Subnets | List, create, update (name, description, per-tenancy access), delete | — | — |
| Subnets | List, create, update (name, description), delete, view stats | List, create, update (name, description), delete, view stats | — |
| Subnet Detail | Stats, exclusion ranges (list, add, update description, delete), allocations (list, filter by tag, release) | Stats, exclusion ranges (list, add, update description, delete), allocations (list, filter by tag, release) | — |
| Allocations | — | Allocate single, bulk allocate, release, manage tags (full replace), filter by tag | Allocate single, bulk allocate, release, manage own tags (full replace), filter by tag |
| Audit Log | Full system log | Scoped to own tenancy | — |

---

## Dashboard

### `GET /dashboard/global` — GlobalAdmin

```json
{
  "tenancyCount": 0,
  "userCount": 0,
  "sharedSubnetCount": 0,
  "sharedSubnetUtilisation": {
    "totalIps": 0,
    "allocatedIps": 0,
    "freeIps": 0,
    "excludedIps": 0,
    "utilisationPercent": 0.0
  },
  "subnetsApproachingExhaustion": [
    {
      "subnetId": "guid",
      "cidr": "string",
      "tenancyId": "guid",
      "tenancyName": "string",
      "utilisationPercent": 0.0
    }
  ],
  "recentAuditEntries": [
    {
      "id": "guid",
      "timestamp": "utc-datetime",
      "action": "string",
      "performedBy": "string",
      "tenancyName": "string",
      "detail": "string"
    }
  ]
}
```

### `GET /dashboard/tenant` — TenantAdmin

```json
{
  "tenancyId": "guid",
  "tenancyName": "string",
  "userCount": 0,
  "privateSubnetCount": 0,
  "privateSubnetUtilisation": {
    "totalIps": 0,
    "allocatedIps": 0,
    "freeIps": 0,
    "excludedIps": 0,
    "utilisationPercent": 0.0
  },
  "accessibleSharedSubnetCount": 0,
  "subnetsApproachingExhaustion": [
    {
      "subnetId": "guid",
      "cidr": "string",
      "utilisationPercent": 0.0
    }
  ],
  "recentAuditEntries": [
    {
      "id": "guid",
      "timestamp": "utc-datetime",
      "action": "string",
      "performedBy": "string",
      "detail": "string"
    }
  ]
}
```

### `GET /dashboard/user` — TenantUser

```json
{
  "recentAccessibleAllocations": [
    {
      "id": "guid",
      "ipAddress": "string",
      "subnetCidr": "string",
      "allocatedAt": "utc-datetime",
      "tags": {}
    }
  ],
  "accessibleSubnets": [
    {
      "subnetId": "guid",
      "cidr": "string",
      "freeIps": 0
    }
  ]
}
```

### Dashboard Behaviour

- Exhaustion threshold — configurable via `Dashboard:ExhaustionThresholdPercent` (default `80.0`)
- Recent audit entries — 10 per dashboard
- User dashboard allocations — all allocations accessible to the authenticated user

---

## New and Changed API Endpoints (Phase 1)

### New auth endpoints

| Method | Route | Access | Description |
|---|---|---|---|
| `POST` | `/auth/login` | Public | Validates Basic Auth credentials, issues encrypted ASP.NET Core cookie |
| `POST` | `/auth/logout` | Authenticated | Clears the auth cookie |

### New dashboard endpoints

| Method | Route | Access | Description |
|---|---|---|---|
| `GET` | `/dashboard/global` | GlobalAdmin | System-wide stats + exhaustion alerts + recent audit |
| `GET` | `/dashboard/tenant` | TenantAdmin | Tenancy-scoped stats + exhaustion alerts + recent audit |
| `GET` | `/dashboard/user` | TenantUser | Accessible subnets + recent allocations |

### Updated user endpoint

`PUT /api/users/{id}/password` is **replaced** by:

| Method | Route | Access | Description |
|---|---|---|---|
| `PUT` | `/api/users/{id}` | GlobalAdmin: any; TenantAdmin: own tenancy; TenantUser: own ID only | Update password and/or role. Both fields optional; at least one required. TenantAdmin may not escalate to GlobalAdmin. |

Request body:
```json
{
  "password": "string (optional)",
  "role": "string (optional — TenantAdmin | TenantUser)"
}
```

### New private subnet update endpoint

| Method | Route | Access | Description |
|---|---|---|---|
| `PUT` | `/api/tenancies/{id}/subnets/{subnetId}` | TenantAdmin of that tenancy (or GlobalAdmin) | Update private subnet name and description |

### New exclusion update endpoint

| Method | Route | Access | Description |
|---|---|---|---|
| `PUT` | `/api/subnets/{subnetId}/exclusions/{id}` | GlobalAdmin: shared subnets; TenantAdmin: own private subnets | Update exclusion description |

---

## New API Configuration Keys

| Key | Type | Required | Default | Description |
|---|---|---|---|---|
| `Dashboard:ExhaustionThresholdPercent` | `double` | No | `80.0` | Utilisation % threshold for exhaustion alerts |
| `Ui:Enabled` | `bool` | No | `true` | Whether ASP.NET Core serves the React static files |

### `Ui:Enabled` Behaviour

- `true` — `app.UseStaticFiles()` and `app.MapFallbackToFile("index.html")` are registered
- `false` — those registrations are skipped; the API continues to function normally

---

## ESLint Configuration

### Plugins

| Plugin | Purpose |
|---|---|
| `typescript-eslint` | Strict TypeScript rules |
| `eslint-plugin-react` | React-specific rules |
| `eslint-plugin-react-hooks` | Hooks rules |
| `eslint-plugin-jsx-a11y` | Accessibility |
| `eslint-plugin-import` | Import ordering + no unresolved |

### Rule Posture

Base presets: `typescript-eslint/strict` + `typescript-eslint/stylistic`

| Rule | Level |
|---|---|
| `no-explicit-any` | error |
| `no-floating-promises` | error |
| `strict-boolean-expressions` | error |
| `no-unused-vars` | error |
| `react-hooks/exhaustive-deps` | error |
| `jsx-a11y` recommended | error |
| `no-non-null-assertion` | warn |
| `consistent-type-imports` | error (auto-fixable) |

---

## Pre-commit Hooks

| Step | Command | Scope |
|---|---|---|
| Lint | `eslint` (no `--fix`) | Staged `.ts`, `.tsx` files only |
| Type check | `tsgo --noEmit` | Whole project |

- No auto-fixing — hook surfaces issues only; developer resolves manually
- Prettier is installed as a formatter but is not run in the pre-commit hook
- `typescript-go` (`tsgo`) used for type checking only; Vite build pipeline continues to use `tsc`
- `husky` initialised via `prepare` script in `package.json`
- `packageManager` field pinned in `package.json` to enforce `pnpm`

---

## Implementation Phases

| Phase | Scope |
|---|---|
| 1 | API — cookie auth scheme, `POST /auth/login`, `POST /auth/logout`, dashboard endpoints, `PUT /api/users/{id}` (replaces password endpoint), `PUT /api/tenancies/{id}/subnets/{subnetId}`, `PUT /api/subnets/{subnetId}/exclusions/{id}`, new config keys (`Dashboard:ExhaustionThresholdPercent`, `Ui:Enabled`) |
| 2 | React scaffold — Vite, TypeScript 6, Carbon, TanStack Router + Query, Zod, `@leomylonas/json-fetch-client`, ESLint 9, Prettier, Husky, lint-staged, pnpm |
| 3 | Feature pages — iterative implementation per role scope, including create / update / delete for all applicable resources |
| 4 | Static asset serving — ASP.NET Core serves Vite `dist/` behind `Ui:Enabled` flag |
| 5 | Docker + CI — multi-stage Dockerfile with Node build stage preceding the .NET publish stage |
