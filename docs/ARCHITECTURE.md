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

## Vivat Flow Control Plane

Vivat Flow is evolving into a multi-tenant SaaS modular monolith. `Organization` represents a Vivat Flow customer/tenant; `Customer` remains an Organization's business customer. The Control Plane is a separate `/api/platform` and `/plataforma` boundary for platform administrators, who are distinct from tenant `ADMIN` users and have no tenant operational permissions. It manages Organization identity and lifecycle only. Suspended and deactivated Organizations cannot use tenant APIs. Subscriptions, entitlements, billing, Service Lines, and aggregate operational-data isolation remain future work.

### Control Plane privacy and LGPD posture

The Control Plane follows privacy-by-design and security-by-design principles. It intentionally exposes only Organization account metadata needed to administer the SaaS service; it does not expose tenant Customers, Services, Quotes, Contracts, Scheduling, Work Orders, documents, or other operational content. Tenant isolation and least privilege are mandatory: a Platform Administrator is not a tenant `ADMIN`, and tenant roles grant no platform access. Lifecycle audit records retain the minimum accountable event context and must never contain passwords, tokens, secrets, or unnecessary personal data. This technical architecture supports LGPD obligations but does not by itself establish full legal compliance, which also depends on organizational and legal processes.

Customers, Service Catalog, Quotes, Quote Visits, and tenant User Administration are tenant-isolated. Each request resolves the authenticated user's active Organization server-side; EF Core applies organization filters to tenant-owned aggregate roots and assigns ownership on persistence. Customer, Service, Quote, and eligible-professional lookups therefore cannot cross the authenticated Organization boundary. Child records derive ownership from their tenant-owned parent.

Remaining tenancy debt is explicit: Contracts, Work Orders, documents, and future operational aggregates have not yet received complete Organization-level data isolation. The Control Plane must not provide platform access to operational records, and Platform Administrators are denied tenant operational API routes.

## Internationalization foundation

Vivat Flow supports `pt-BR` and `en-US`, with `en-US` as fallback. Locale resolution is user-specific: an explicit saved user preference wins, followed by an explicit browser-local preference, browser language detection, and fallback. The backend persists only validated `PreferredLocale` values on the user profile; technical identifiers and domain enums remain language-neutral, with localized presentation in the frontend. The same foundation is used by tenant and Platform Administrator experiences.

User administration is an ADMIN-only API boundary. MVP 1 users have exactly one application role and are deactivated rather than deleted. Server-generated temporary passwords are returned only by their create/reset response and are never persisted outside Identity's password hash. A small `UserAdministrationAuditRecord` persists security events without passwords or tokens. Security-stamp validation occurs on every request so deactivation, role changes, and password resets invalidate stale sessions promptly.
