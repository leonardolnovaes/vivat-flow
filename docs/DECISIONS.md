# Architecture decisions

Lightweight ADR log. Earlier entries remain useful history; later entries explicitly supersede outdated product assumptions.

## ADR-001 — Modular monolith

**Decision:** One application with clear module boundaries.  
**Why:** Simple deployment without sacrificing domain ownership; microservices add unjustified operational cost.

## ADR-002 — ASP.NET Core backend

**Decision:** ASP.NET Core and C#.  
**Why:** Mature support for APIs, validation, authorization, Identity, and EF Core.

## ADR-003 — React TypeScript frontend

**Decision:** React, TypeScript, and Vite.  
**Why:** Productive typed web UI foundation.

## ADR-004 — PostgreSQL

**Decision:** PostgreSQL through Entity Framework Core.  
**Why:** Reliable relational storage for connected operational workflows.

## ADR-005 — ASP.NET Core Identity

**Decision:** Identity manages users, credentials, and roles.  
**Why:** Established password and authorization foundations without custom auth infrastructure.

## ADR-006 — Cookie authentication

**Decision:** Secure HttpOnly cookie-based authentication, with no public registration.  
**Why:** Suits a first-party application and avoids browser localStorage tokens.

## ADR-007 — Docker Compose locally

**Decision:** Use Docker Compose for local dependencies when introduced.  
**Why:** Reproducible local dependencies without premature production orchestration.

## ADR-008 — Private object storage for documents

**Decision:** Keep future document metadata in PostgreSQL and binaries in private S3-compatible storage; use MinIO locally when needed.  
**Why:** Separates business context from binary storage and supports secure file access.

## ADR-009 — No financial module in the initial operational MVP

**Decision:** Exclude payables, receivables, banking, reconciliation, and invoice workflows from the initial operational MVP.  
**Why:** Commercial values can exist without introducing a full financial subsystem.

## ADR-010 — Delivery separate from execution

**Decision:** Model future delivery independently from Work Order operational completion.  
**Why:** Completed work may still require delivery and must remain visible.

## ADR-011 — Pilot not hardcoded

**Decision:** Do not hardcode CDS-specific behavior, names, or rules.  
**Why:** The pilot informs requirements but is not a product dependency.

> Superseded in scope by ADR-019: the platform is not limited to SST companies.

## ADR-012 — English technical artifacts and Portuguese UI

**Decision:** Use English for source code and technical artifacts. Use Portuguese (Brazil) for user-facing application content, structured for future localization.  
**Why:** English technical naming keeps the codebase consistent while pt-BR serves current users.

## ADR-013 — Secure initial ADMIN provisioning

**Decision:** Startup may bootstrap one tenant ADMIN only from environment/configuration secrets when the tenant has no user. Bootstrap is idempotent, does not reset existing administrators, and requires initial password change.  
**Why:** The system needs secure initial access without public registration.

## ADR-014 — Documents have delivery intent

**Decision:** Future Documents distinguish customer deliverables from internal/supporting files; Delivery records exactly what was delivered.  
**Why:** Not every file linked to work is customer-facing.

## ADR-015 — Identity cookie and anti-forgery protection

**Decision:** Use ASP.NET Core Identity cookies with an antiforgery token issued by `GET /api/auth/csrf` and required on state-changing requests.  
**Why:** Cookie-authenticated mutations require explicit CSRF protection.

## ADR-016 — Explicit user administration lifecycle

**Decision:** Users require `FullName`, have one application role in the current model, and are deactivated rather than physically deleted. Temporary passwords are generated server-side and revealed only by their create/reset response.  
**Why:** Supports accountable assignments and avoids insecure credential handling.

## ADR-017 — Quote commercial boundary and authorization

**Decision:** Quotes are commercial/management records. The current `CommercialAdmin` policy is ADMIN-only. Quotes retain historical customer/service context and explicit commercial lifecycle state.  
**Why:** Commercial approval must remain controlled and historically understandable.

## ADR-018 — Control Plane privacy boundary

**Decision:** Manage Organization identity, lifecycle, and platform-level Service Line enablement through a separate Control Plane. Platform Administrators are separate from tenant ADMIN users and cannot operate tenant business data.  
**Why:** Platform administration needs account-level control without unnecessary visibility into tenant operational or personal data.

## ADR-019 — Vivat Flow is a generic multi-vertical SaaS platform

**Decision:** The product CORE is generic for service companies. SST/TST, Cleaning, Flooring, clinics, and other verticals are represented through configuration, Service Lines, or explicit vertical extensions rather than separate products or hardcoded core rules.  
**Why:** One platform should serve multiple service businesses and allow one Organization to operate multiple lines without duplicated systems.

**Supersedes:** the SST-only scope implied by earlier product descriptions.

## ADR-020 — Organization is the tenant root

**Decision:** Organization owns tenant operational data. Platform Administrator and tenant roles are separate security boundaries. Current tenant users belong to one Organization; cross-Organization membership/switching is deferred.  
**Why:** Tenant ownership must be explicit and backend-enforced before the platform can safely scale to multiple customers.

## ADR-021 — Service Lines classify Services, not Quotes or Contracts

**Decision:** A Service belongs to exactly one Service Line. Quotes and Contracts may contain items from multiple Service Lines and therefore do not have a top-level `ServiceLineId`.  
**Why:** A single commercial agreement may combine multiple kinds of service.

## ADR-022 — Historical Service Line snapshots

**Decision:** Quote items snapshot the Service and Service Line context used commercially. Contract items copy those snapshots from the approved Quote rather than reading the current Service Catalog.  
**Why:** Catalog reclassification or disablement must not rewrite what was approved or contracted.

## ADR-023 — Contract creation is explicit after Quote approval

**Decision:** Quote approval does not automatically create or activate a Contract. An authorized user explicitly creates a Draft Contract from an approved Quote; inherited commercial scope is read-only and formalization metadata is separate.  
**Why:** Approval and formal contractual activation are distinct business events, and some future workflows may execute without requiring a Contract.
