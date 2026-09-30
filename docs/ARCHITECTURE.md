# Architecture

## Approach

Vivat Flow is a pragmatic multi-tenant modular monolith: one deployable application with feature-oriented module boundaries. This keeps development and deployment simple while preserving clear ownership.

Do not introduce microservices, generic repository layers, CQRS frameworks, event sourcing, message brokers, or additional infrastructure without a demonstrated need.

The historical `Tsdt` technical namespace remains in the current codebase. Product behavior is Vivat Flow; renaming the technical namespace is a separate refactor.

## Core versus vertical extensions

The CORE contains concepts reusable across service businesses: Identity, Organizations, Customers, Service Lines, Service Catalog, Quotes, Contracts, future Work Orders, Documents, Notifications, Audit, and related platform capabilities.

Vertical-specific SST, Cleaning, Flooring, clinic, or other rules must not contaminate the CORE. Add vertical behavior only through explicit configuration or extensions when a generic model cannot represent it cleanly.

See [PRODUCT_MODEL.md](PRODUCT_MODEL.md) for canonical domain boundaries.

## Module boundaries

| Module | Owns |
| --- | --- |
| Authentication & Administration | identities, roles, access, user lifecycle |
| Control Plane | Organization lifecycle and platform-level Service Line enablement |
| Customers | tenant customers, contacts, units |
| Service Catalog | tenant service definitions |
| Service Lines | global lines and Organization enablement |
| Quotes | commercial proposals, items, approval lifecycle, assignment/visits |
| Contracts | explicit formalization of approved Quotes and immutable commercial scope |
| Work Orders | future execution, assignments, dates, operational status |
| Documents | future document metadata, context, versions, storage references |
| Deliveries | future delivery records and evidence |
| Dashboard | future operational projections/actionable lists |
| Audit | accountable important-action records |

## Tenant boundary

`Organization` is the tenant root. `Customer` is a business customer inside one Organization.

Tenant-owned aggregate roots are resolved server-side from the authenticated user's Organization. Backend authorization and tenant filters are authoritative; the frontend is not a security boundary.

Current tenant ownership covers Customers, Services, Quotes, Contracts, eligible-professional lookups, and their owned child records. Future operational aggregates must implement the same boundary before release.

Platform Administrators are separate from tenant users. Platform access must never imply operational tenant access.

## Service Lines

Service Lines are global platform catalog entries with stable technical codes. The Control Plane enables or disables lines per Organization.

A tenant Service references exactly one Service Line. New selection requires the line to be globally active and enabled for that Organization.

Quotes and Contracts may contain items from multiple Service Lines. There is intentionally no Quote-level or Contract-level ServiceLineId.

Historical Quote and Contract item snapshots preserve Service Line identity/code/name so later catalog changes do not rewrite approved history.

## Quote and Contract boundary

Quotes represent the commercial proposal: what will be done, for how much, and under which commercial conditions.

Contracts represent explicit formalization after approval: what was contracted, for what period, and under which formal terms.

Approval does not auto-create a Contract. Contract creation is an explicit user action. Contract scope is inherited from the approved Quote and is not silently editable.

Work Orders are a separate future execution boundary and must not be folded into Contracts merely for convenience.

## Backend and data

The backend uses ASP.NET Core/C#, Entity Framework Core, PostgreSQL, and ASP.NET Core Identity. PostgreSQL is the system of record. Migrations are the only supported schema evolution mechanism.

This first-party web application uses secure HttpOnly cookie authentication and antiforgery protection. No public registration exists.

Secrets come from environment/local ignored configuration and must never be committed or logged.

## Authorization

Backend authorization is authoritative.

Tenant commercial data currently follows explicit module policies; Platform Administrators cannot access tenant operational APIs. Future execution permissions should expose only the operational scope required for assigned work.

Role or phase changes must not be implemented only in the frontend.

## Privacy, security, and LGPD posture

Privacy-by-design and security-by-design are mandatory across modules, APIs, database schema, logs, dashboards, and integrations.

- Minimize personal data.
- Keep purpose explicit.
- Enforce tenant isolation and least privilege.
- Do not expose secrets or sensitive data in logs, errors, or dashboards.
- Keep Control Plane visibility superficial and account-oriented.
- Do not add medical/sensitive worker records without explicit approved scope.

These technical controls support LGPD obligations but do not replace organizational/legal compliance processes.

## Audit

Audit important security and business transitions with actor, action, entity reference, time, and minimized context.

Never audit passwords, tokens, secrets, or unnecessary personal data.

## Frontend

The React/TypeScript/Vite frontend is organized by product module and consumes backend APIs.

User-facing content is pt-BR. Technical identifiers, source code, APIs where reasonable, tests, logs, and developer documentation are English.

All meaningful frontend work follows [FRONTEND_STANDARDS.md](FRONTEND_STANDARDS.md).

## Testing and validation

xUnit tests use `Category=Unit` or `Category=Integration`.

Normal AI-assisted validation may execute only targeted unit tests plus proportional static/build/lint checks. E2E, integration, smoke, regression, performance, and PostgreSQL-specific suites are manual/CI unless explicitly authorized.

`scripts/validate.ps1` is canonical combined validation but is opt-in and runs only when the user explicitly requests it.

## Runtime environments

DEV, DEMO, and PREVIEW have distinct lifecycle and persistence guarantees. See the canonical [ENVIRONMENTS.md](ENVIRONMENTS.md) contract.
