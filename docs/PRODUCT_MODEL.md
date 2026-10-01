# Vivat Flow product model

This document is the canonical product-domain reference for Vivat Flow. It defines the concepts that should remain stable across verticals. Feature-specific prompts may refine behavior, but they should not silently contradict these boundaries.

## Product direction

Vivat Flow is a generic multi-tenant SaaS platform for companies that sell and execute services.

The CORE must remain reusable. Vertical-specific behavior belongs in configuration, Service Lines, or explicit vertical extensions when the generic model is insufficient. SST/TST is an important vertical and pilot, not the definition of the platform.

Examples of verticals that can coexist in the same product include SST, Cleaning, Flooring, clinics, and other service businesses.

## Tenant model

An `Organization` is a Vivat Flow customer/tenant.

A tenant's `Customer` is that Organization's own business customer. Platform operators must never confuse an Organization with a tenant Customer.

Current tenant users belong to one Organization. Multi-Organization user membership/switching is not implemented and must not be invented without an explicit architecture decision.

Tenant operational records must always be isolated by Organization on the backend. Frontend visibility is not a security boundary.

## Control Plane

The Vivat Flow Control Plane manages the platform, not the tenant's business operation.

Platform Administrators may manage Organization identity, lifecycle, platform access, and Organization-to-Service-Line enablement. They must not gain operational access to tenant Customers, Services, Quotes, Contracts, Work Orders, documents, or other business records.

The Control Plane administers the board; tenant users operate the business.

## Service Lines and Services

`ServiceLine` is a global platform capability/category such as SST, Cleaning, or Flooring.

`OrganizationServiceLine` enables a global Service Line for a specific Organization.

A tenant `Service` belongs to exactly one Service Line. Creating or moving a Service to a line requires that the line be globally active and enabled for the tenant.

Disabling a Service Line must prevent new use where appropriate without erasing historical business data.

## Quotes

A `Quote` belongs to one Organization and one Customer.

A Quote contains one or more `QuoteItem` records that reference Services. A single Quote may contain Services from multiple Service Lines.

Do **not** add `Quote.ServiceLineId`. Classification comes from the items.

Commercial snapshots must preserve what the customer actually reviewed. Service code/name and Service Line identity/code/name stored on a Quote item are historical data and must not silently change when the current Service Catalog changes.

The current commercial lifecycle supports Draft, Awaiting Approval, Changes Requested, Approved, Rejected, Expired, and Cancelled states according to the backend rules.

## Contracts

A Contract is an explicit formalization of an approved Quote. Quote approval must not automatically create or activate a Contract.

The user explicitly chooses to create a Contract from an approved Quote. The approved commercial scope is inherited and remains read-only; contract formalization adds supported metadata such as dates, terms, and notes.

A Contract may contain Services from multiple Service Lines. Do **not** add `Contract.ServiceLineId`.

Contracts have two commercial kinds: `OneOff` (Pontual) for defined, non-recurring engagements and `Recurring` (Recorrente) for ongoing or period-based service relationships. A Customer may have multiple independent Contracts, including simultaneous or successive Contracts of different kinds. Both kinds keep the Customer active while the Contract is Active. An Active Contract can originate successive Work Orders; only one unfinished Work Order may cover the same Contract scope at a time. An unfinished legacy Quote-origin Work Order without a Contract also blocks new execution for Contracts originating from that Quote until it closes or is cancelled. Contract closure ends its own active relationship without erasing other active Quotes or execution.

Contract items preserve historical Service and Service Line snapshots copied from the approved Quote items, not from the mutable current Service Catalog.

Current Contract lifecycle:

- Draft
- Active
- Ended
- Cancelled

Activation requires a start date. Invalid backward or repeated transitions are rejected by the backend.

## Work Orders and execution

Work Orders are the explicit execution boundary, separate from Quote and Contract commercial formalization. New Work Orders are created only from an Active Contract. Approval, formalization, and activation never create a Work Order automatically. An Active Contract can originate successive Work Orders after the previous one closes or is cancelled; simultaneous unfinished Work Orders for the same Contract scope remain blocked. Historical Quote-origin Work Orders remain readable but cannot be created by current APIs.

New Work Orders snapshot the service address from the originating Quote and the operational item scope from the Contract. Historical Quote-origin Work Orders retain their original snapshots. Operational responses exclude prices and payment terms. The lifecycle is Draft, Scheduled, InProgress, AwaitingClosure, Closed, or Cancelled. Execution completion means the assigned professional finished work; management closure is a separate acceptance action. Delivery remains independent.

ADMIN and MANAGER manage tenant Work Orders and may perform execution actions. USER can see only assigned Work Orders and may start or complete their own work. Platform Administrators have no Work Order access. Recurrence, partial execution, and multiple assignees are outside this MVP.

## Agenda

Agenda reads Work Orders, never duplicates their scheduling data. `GET /api/work-orders/agenda` requires offset-aware `from` and `to` timestamps for a forward interval of at most 62 days. Results overlap `[from, to)` when `ScheduledStart < to && ScheduledEnd > from`, have valid schedules, and are Scheduled, InProgress, AwaitingClosure, or Closed. Ordering is start, end, number, then id; bounded results are not paginated. Services, customer, address, and responsible name come from historical Work Order snapshots.

ADMIN/MANAGER see their Organization's entries and may filter by professional. USER is always scoped to authenticated assignment, ignoring requested assignee filters. Platform Administrators have no operational access. Browser-local day/week/month boundaries are sent as ISO instants. Agenda links to Work Order details; editing, recurrence, synchronization, notifications, and conflict prevention are outside this MVP.

## Historical integrity

Historical commercial and contractual records must remain readable even when current configuration changes.

Examples:

- a Service becomes inactive;
- a Service moves to another Service Line;
- an Organization Service Line is disabled;
- a global Service Line is deactivated.

Current catalog state may affect new selection, but it must not rewrite historical Quote or Contract meaning.

## Privacy and security

Every module must use privacy-by-design and security-by-design.

Minimum expectations include tenant isolation, least privilege, data minimization, explicit purpose, secure defaults, no sensitive data in logs/errors/dashboards, and no platform-level browsing of tenant operational data.

LGPD-aware technical design supports compliance but does not by itself establish legal compliance.
