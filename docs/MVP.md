# MVP 1

## Problem and objective

SST companies frequently lose visibility between customer request, quote, approval, execution, document completion, and delivery because work is spread across spreadsheets and folders. MVP 1 is the operational system of record for this path and must make completed work awaiting delivery impossible to overlook.

TSDT ERP is reusable for Brazilian SST companies; CDS – Treinamento e Segurança do Trabalho is pilot context only.

## Modules in scope

1. Authentication and Administration
2. Customers
3. Service Catalog
4. Quotes
5. Contracts (basic)
6. Service Orders
7. Documents
8. Deliveries
9. Dashboard
10. Audit

Authentication includes login/logout, no public registration, ADMIN-only user creation, activation/deactivation, and ADMIN/MANAGER/USER roles. Backend authorization is required.

Customers hold company details, CNPJ, names, contacts, units, status, and notes. Services are configurable catalog data; behavior must not depend on service names.

Quotes belong to customers, have multiple items, and follow `DRAFT → SENT → APPROVED`, or `REJECTED` / `CANCELLED`. Approved quotes may create one or more service orders. Basic contracts hold customer, dates, status, optional monthly value, notes, and covered services; they may create many orders but do not provide recurring billing.

Service Orders own customer/unit, quote-or-contract origin, service items, responsible users, dates, pending items, notes, and operational status: `PENDING`, `IN_PROGRESS`, `WAITING_CUSTOMER`, `COMPLETED`, or `CANCELLED`.

Documents always have customer and service-order context, and may have service-order-item context. They include metadata, delivery intent (customer deliverable or internal/supporting), and support future versioning. Deliveries separately record service order, the one or more documents actually delivered, date, method (`EMAIL`, `WHATSAPP`, `IN_PERSON`, `PORTAL`, `MAIL`, `OTHER`), recipient, user, notes, and optional evidence. Not every attached document must be delivered.

The dashboard prioritizes actionable lists/counters: in progress, overdue, waiting for customer, completed awaiting delivery, and recently delivered. Audit captures who did what to which entity and when for important actions.

## Primary flows

One-time work: `Customer → Quote → Approved → Service Order → Execution → Document Ready → Delivery Registered → Completed`.

Recurring work: `Customer → Recurring Contract → one or more Service Orders → Execution → Document → Delivery`.

A Service Order has either an approved-quote or contract origin. Operational completion and delivery are separate: completed work can still have delivery pending.

## Acceptance scenario

1. An admin logs in and creates another user.
2. A customer and PGR catalog service are registered.
3. A quote containing PGR is created and approved.
4. A service order is created, assigned, and moved to `IN_PROGRESS`.
5. A document is uploaded and work is marked `COMPLETED`.
6. The dashboard shows it as completed awaiting delivery.
7. Delivery date, method, recipient, and delivering user are registered.
8. It no longer awaits delivery, and audit history shows the important journey events.

## Explicitly out of scope

- Payables, receivables, banking, reconciliation, invoices, boletos, suppliers, and partners
- Customer portal, WhatsApp integration, automatic email, and notification engine
- eSocial, automated document generation, OCR, AI, and native mobile apps
- Worker health/medical records, ASO, PPE, risk management, and dedicated training management
- Kubernetes, microservices, message brokers, Redis, and Elasticsearch

Training may be a catalog service. Quote and contract values may exist, but no financial module exists in MVP 1.

## Completion criteria

Authorized users can complete the acceptance scenario end to end; important actions are audited; and the dashboard reliably highlights completed service orders whose required document delivery has not been registered.
