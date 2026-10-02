# Vivat Flow MVP scope

## Objective

Vivat Flow is the operational system of record for service companies, covering the journey from tenant setup and customer registration through commercial proposal, formalization, future execution, documents, delivery, and operational visibility.

The product CORE is generic. SST/TST is the first vertical and CDS is the first pilot context, but neither defines the core data model.

## Core modules

1. Authentication and tenant user administration
2. Control Plane and Organizations
3. Customers, contacts, and units
4. Service Lines and Service Catalog
5. Quotes, commercial approval, assignment, and visits
6. Contracts
7. Work Orders and Agenda (day, week, month)
8. Documents
9. Deliveries
10. Dashboard
11. Audit

## Commercial flow

A Customer may receive a Quote containing multiple Services, including Services from different Service Lines.

The commercial journey is conceptually:

`Customer → Quote → Approval → Contract formalization and activation → Work Order execution` is the required journey. A new Work Order must originate from an Active Contract; an approved Quote cannot originate a new Work Order directly.

A Contract is never auto-created by approval. Contract scope comes from the approved Quote and remains historically stable.

Commercial values may exist, but the initial operational MVP does not implement receivables, banking, invoicing, or a full billing engine.

## Execution flow

Work Orders own execution-specific state: assignment, dates, operational progress, and notes. Management creates a Work Order explicitly from an Active Contract. Historical Quote-origin Work Orders remain readable, but new Work Orders cannot bypass Contract formalization. Operational completion is terminal in this MVP; there is no separate management closure action. Delivery remains independent.

Do not collapse Work Orders into Quote or Contract merely to accelerate implementation. Commercial formalization and operational execution are separate boundaries.

Agenda is the read/navigation surface between Work Order planning and execution. It uses Work Order schedules and historical snapshots, with no separate appointment aggregate. Scheduled, InProgress, Completed, and historically scheduled Cancelled orders appear; Draft does not. Start date and assignee are required to schedule; time and end date are optional. Management can filter the Organization's schedule by eligible professional; USER sees only assigned work. Scheduling changes remain in the Work Order planning flow.

The intended permission direction is phase-aware: management owns commercial/formalization phases, operational users receive only the execution scope required for assigned work, and future delivery receipt controls remain separate from execution.

## Documents and delivery

The first Documents phase stores customer-owned metadata and optional business context in PostgreSQL and binary content through private local storage configured for the current deployment. The storage interface permits a later cloud provider without changing the document model. Upload, customer list, and authorized download are backend-only in this phase; contextual UI, customer document aggregation UI, search, versioning, and removal are future work.

Documents distinguish customer deliverables from internal/supporting files.

Delivery is separate from execution completion: completed work may still be awaiting customer delivery.

## Control Plane

The Vivat Flow Control Plane manages Organizations and platform capability enablement without operating tenant business data.

Platform Administrators do not browse tenant Customers, Services, Quotes, Contracts, Work Orders, or documents.

## Multi-vertical behavior

One Organization may operate multiple Service Lines, for example Cleaning and Flooring, while sharing Customers and users.

A single Quote or Contract may include Services from multiple Service Lines.

The CORE must not contain behavior keyed to names such as PGR, PCMSO, LTCAT, Cleaning, or Flooring. Vertical-specific behavior requires explicit extension/configuration.

## Explicitly out of scope for the initial operational MVP

- full finance: payables, receivables, banking, reconciliation, invoices, boletos
- supplier/partner management unless later required
- customer portal
- automatic WhatsApp/email notification engine
- optional "Copiar mensagem para e-mail" Quote template with Quote context and selected Customer Contact recipients; intended recipients are recorded now, while delivery and template generation are future work
- eSocial integrations
- automated document generation/OCR/AI workflows
- worker medical records or other sensitive health datasets without explicit approved scope
- native mobile applications
- microservices, Kubernetes, message brokers, Redis, or Elasticsearch without demonstrated need
- electronic Contract signatures, generated legal PDFs, amendments, renewals, recurring execution, or Work Order creation as incidental Contract features

## Acceptance direction

The MVP should allow an authorized tenant to:

1. authenticate and administer users;
2. maintain Customers and units;
3. configure Services under enabled Service Lines;
4. create and approve a multi-item Quote;
5. explicitly formalize and activate an approved Quote into a Contract before creating a Work Order;
6. execute Work Orders without losing tenant or commercial context;
7. attach future Documents and register Delivery independently from completion;
8. preserve important audit history and actionable operational visibility.

Each module is accepted only after its implementation, authorization, validation, UX, and manual QA are appropriate for its risk.
