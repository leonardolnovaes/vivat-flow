# Architecture decisions

Lightweight ADR log for MVP 1 decisions; later decisions may supersede an entry.

## ADR-001 — Modular monolith

**Decision:** One application with clear module boundaries.  
**Why:** Simple MVP deployment without sacrificing domain ownership; microservices add unjustified operational cost.

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

**Decision:** Use Docker Compose for local services when introduced.  
**Why:** Reproducible local dependencies without premature production orchestration.

## ADR-008 — Private object storage for documents

**Decision:** Keep document metadata in PostgreSQL and binaries in private S3-compatible storage; use MinIO locally when needed.  
**Why:** Separates business context from binary storage and supports secure file access.

## ADR-009 — No financial module in MVP 1

**Decision:** Exclude financial operations.  
**Why:** The MVP proves operational visibility; quote/contract values do not require billing workflows.

## ADR-010 — Delivery separate from execution

**Decision:** Model delivery independently from Service Order operational status.  
**Why:** Completed work may still require document delivery, which must remain visible.

## ADR-011 — Pilot not hardcoded

**Decision:** Do not hardcode CDS-specific behavior, names, or rules.  
**Why:** The product serves multiple SST companies; the pilot informs requirements only.

## ADR-012 — English technical artifacts and Portuguese UI

**Decision:** Use English for source code and all technical artifacts. Use Portuguese (Brazil) for user-facing application content, organized for future localization.  
**Why:** English technical naming keeps the codebase consistent and accessible, while pt-BR serves the MVP users.

## ADR-013 — Secure initial ADMIN provisioning

**Decision:** When no users exist, startup may bootstrap one ADMIN only from environment/configuration secrets. The bootstrap is idempotent, never resets an existing administrator, requires password change at first login, and creates no account when credentials are absent.  
**Why:** The system needs an initial administrator without a public registration endpoint or insecure defaults.

## ADR-014 — Documents have delivery intent

**Decision:** Documents distinguish customer deliverables from internal/supporting files. A Delivery references the one or more documents actually delivered; delivery remains separate from execution status.  
**Why:** Not every file linked to a service order is intended for the customer, and a completed order can still await delivery.

## ADR-015 — Identity cookie and anti-forgery protection

**Decision:** Use ASP.NET Core Identity cookies with an anti-forgery token issued by `GET /api/auth/csrf` and required on every state-changing authentication request.
**Why:** The first-party React app does not need browser-stored bearer tokens, while explicit anti-forgery validation protects cookie-authenticated mutations.

## ADR-016 - Explicit MVP user administration lifecycle

**Decision:** Application users require `FullName`, have exactly one role in MVP 1, and are deactivated rather than deleted. ADMIN routes generate temporary passwords server-side and reveal them only once. The last active ADMIN cannot be deactivated or moved to another role.
**Why:** This supports accountable operational assignments while keeping initial administration small and preventing accidental loss of administrative access. Automated recovery and email delivery remain out of scope.

## ADR-017 — Quote commercial boundary and authorization

**Decision:** Quotes are commercial/management records. The `CommercialAdmin` policy is ADMIN-only in this module; operational USER and MANAGER accounts have no Quote access. Quotes retain immutable customer/service snapshots, use São Paulo annual numbering, and allow only `Draft → AwaitingApproval`, explicit reopen to Draft, then approval, rejection, or cancellation. Execution authorization is outside this module.

**Why:** Commercial approval must remain controlled while preserving the proposal context that was actually reviewed.
