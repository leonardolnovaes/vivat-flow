# Architecture

## Approach

TSDT ERP will be a pragmatic modular monolith: one deployable application with feature-oriented module boundaries. This keeps MVP development and deployment simple while making ownership clear. Do not add microservices, generic repositories, CQRS frameworks, event sourcing, or extra infrastructure without a demonstrated need.

## Proposed repository structure

```text
backend/          ASP.NET Core application and backend tests
frontend/         React application
e2e/              Playwright tests
infrastructure/   local Docker Compose configuration
docs/             product and architecture documentation
```

## Module boundaries and domain model

| Module | Owns |
| --- | --- |
| Authentication & Administration | identities, roles, access, user lifecycle |
| Customers | customers, contacts, units |
| Service Catalog | configurable service definitions |
| Quotes | quotes, items, lifecycle |
| Contracts | recurring agreements and covered services |
| Service Orders | work, items, assignments, dates, operational status |
| Documents | document metadata, context, versions, storage references |
| Deliveries | delivery records, document associations, evidence references |
| Dashboard | operational projections and actionable lists |
| Audit | immutable important-action records |

A Customer has contacts and units. Quotes have items that reference catalog services; an approved Quote may originate one or more Service Orders. A Contract has covered services and may originate many Service Orders. Each Service Order belongs to a customer, optionally a unit, and has exactly one origin: approved quote or contract. It has items, responsible users, dates, status, pending items, and notes. Documents have customer/service-order context and optional item context. Deliveries belong to an order and associate delivered documents. Audit entries retain actor, action, entity type/identifier, time, and minimized context.

Operational status is independent of delivery. “Completed, awaiting delivery” is a dashboard projection of completed work without required registered delivery.

Documents must distinguish customer deliverables from internal, draft, working, or evidence files. A delivery references one or more documents actually delivered; it does not imply every document on the service order was delivered. The exact delivery-state rule remains intentionally simple for MVP 1 and does not require a workflow engine.

## Backend, data, and access

The backend will use ASP.NET Core/C#, Entity Framework Core, PostgreSQL, and ASP.NET Core Identity. PostgreSQL is the system of record; migrations begin only with implementation. Keep data access explicit and close to module behavior.

This first-party web app uses secure HttpOnly cookie authentication. No public registration exists. Only ADMIN creates users. Backend authorization is authoritative; the frontend is not a security boundary.

When no application users exist, startup may bootstrap the first ADMIN only from environment/configuration secrets. Bootstrap is idempotent, never overwrites an existing administrator, requires an initial password change, and does nothing if required credentials are absent.

## Documents, audit, testing, frontend

Metadata and business context are stored in PostgreSQL. File content will use private S3-compatible object storage, with MinIO expected locally once uploads are implemented. Upload validation, authorization, and safe content handling are mandatory.

Audit records important changes such as user creation, customer changes, quote/status changes, assignments, uploads, and deliveries. Never include passwords, tokens, secrets, or unnecessary sensitive data.

The React/TypeScript/Vite frontend is organized by product module and consumes backend APIs. xUnit tests are classified with `Category=Unit` or `Category=Integration`; the normal AI validation executes only unit tests. Playwright covers end-to-end workflows, especially the acceptance scenario and delivery-pending visibility, and is executed manually or by CI/CD.

All source code and technical artifacts use English identifiers and names, including database objects, APIs where reasonable, tests, comments, filenames, logs, and developer documentation. User-facing application content is Portuguese (Brazil); UI organization should permit clean future localization.

## Security boundaries

- Backend enforces authorization and validation.
- Identity handles passwords; plaintext passwords are never stored.
- Secrets use environment configuration and are never committed or logged.
- Files are private by default; production requires HTTPS.
- Apply data minimization and LGPD-aware design. Medical and sensitive data are out of scope absent explicit approval.

## Identity foundation

`ApplicationDbContext` is the PostgreSQL EF Core context for ASP.NET Core Identity. `ApplicationUser` adds `FullName`, `IsActive`, and `MustChangePassword`; roles are `ADMIN`, `MANAGER`, and `USER`. On startup, migrations are applied and roles are ensured. If no users exist, a configured bootstrap account becomes ADMIN with `MustChangePassword=true`; no existing account is changed.

Authentication uses an HttpOnly, Secure, SameSite=Lax Identity cookie. The React client uses `credentials: 'include'`. `GET /api/auth/me` returns only the safe session profile. `GET /api/auth/csrf` is safe before authentication and issues the anti-forgery cookie plus a minimal request token response. The React API helper obtains and sends that token in `X-CSRF-TOKEN` for every state-changing request, including login; the matching anti-forgery cookie is HttpOnly and set by the backend.

The forced-password-change middleware denies API access for authenticated users whose `MustChangePassword` is true, except the session profile, password change, logout, and CSRF endpoints. Future protected APIs inherit this guard.

User administration is an ADMIN-only API boundary. MVP 1 users have exactly one application role and are deactivated rather than deleted. Server-generated temporary passwords are returned only by their create/reset response and are never persisted outside Identity's password hash. A small `UserAdministrationAuditRecord` persists security events without passwords or tokens. Security-stamp validation occurs on every request so deactivation, role changes, and password resets invalidate stale sessions promptly.
