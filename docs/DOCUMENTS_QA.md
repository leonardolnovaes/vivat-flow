# Documents foundation manual QA

Status: backend foundation ready for code review. Manual authenticated API and storage QA remains pending. Do not use DEMO for this QA.

## API and policy

- `POST /api/customers/{customerId}/documents`: ADMIN or MANAGER only. Send multipart `file`, `category`, required `purpose`, optional `description`, and optional `contextType` plus `contextId`. Both context fields must be present together. The CSRF header is required. Only ADMIN may upload Quote/Contract context or Contract/SignedDocument category.
- `GET /api/customers/{customerId}/documents?page=1&pageSize=25`: ADMIN sees tenant documents. MANAGER sees tenant documents outside Quote/Contract context and Contract/SignedDocument category. USER sees only noncommercial documents linked to a Work Order assigned to that user for an active Customer; page size is capped at 100.
- `GET /api/documents/{id}/download`: Uses the same visibility rule. Response is an attachment with a sanitized original filename and no internal storage key.
- `GET /api/customers/{customerId}/documents/contexts`: ADMIN and MANAGER only. Returns customer-owned upload choices; Quote and Contract choices are ADMIN-only. Document list entries include a customer-scoped context label and uploader name.

Purposes: `InternalSupporting` and `CustomerDeliverable`. Existing documents are classified as `InternalSupporting` by the migration. Categories: `General`, `Report`, `Certificate`, `Contract`, `Evidence`, `Photo`, `SignedDocument`, `Other`. Accepted files in this phase: PDF, PNG, JPEG, and WebP with matching extension, MIME type, and signature. Default maximum size is 10 MiB, configurable through `DocumentStorage:MaxFileSizeBytes` up to 20 MiB; larger values fail at startup. `DocumentStorage:RootPath` selects the private local root; the default is `.local/documents` under the API content root. Back up this root together with PostgreSQL metadata. No cloud storage or permanent removal is implemented.

## Manual checks in DEV

1. As ADMIN/MANAGER, upload a small PDF to an existing Customer without a context. Confirm 201 response, metadata list entry, download filename and bytes, and a `DOCUMENT_UPLOADED` audit record with actor, tenant, customer, and time.
2. Upload documents with Customer, Customer Unit, Quote, Contract, and Work Order contexts. Confirm each context belongs to the route Customer and tenant. Upload to a completed Work Order and verify its status does not change.
3. Attempt another Customer's context and another tenant's Customer/context/document ID. Confirm no foreign metadata or file is disclosed. Test unauthenticated access and USER upload denial. Assign one Work Order to USER and confirm only its noncommercial documents can be listed/downloaded. Confirm unassigned Work Order, Quote/Contract context, Contract/SignedDocument category, and inactive Customer files are hidden from USER. Confirm MANAGER cannot upload or read Quote/Contract context or Contract/SignedDocument category.
4. Try an empty file, a file above the configured limit, an executable, a PDF renamed as an image, and a filename containing directory separators. Confirm safe validation messages and no physical path in any response.
5. Confirm the private storage root contains only generated Organization/key names and is not served as a public web directory. Simulate storage and database failures in an isolated DEV test environment and confirm no metadata row or orphaned file remains.

The targeted `Category=Unit&FullyQualifiedName~Tsdt.Tests.Documents` tests cover the service and local provider. No E2E, integration, smoke, or performance suite was executed by the agent.

## Customer UI manual checks in DEV

1. Open an active Customer as ADMIN. Confirm the Documents section loads, empty guidance appears when applicable, and pagination keeps newest documents first. Upload one PDF with purpose `Entrega ao cliente`, a category, description, and each available context. Confirm the success message, refreshed list, readable context and uploader, and protected download with the original filename.
2. Open the same Customer as MANAGER. Confirm Quote/Contract contexts and Contract/SignedDocument categories are absent. Confirm existing commercial documents are absent, while permitted documents can be uploaded and downloaded.
3. Open an active Customer as USER assigned to one Work Order. Confirm only noncommercial documents for that Work Order appear, with no upload action. Confirm an unassigned USER and an inactive Customer do not expose document metadata or counts. Confirm an expired session, denied request, missing document, failed download, failed upload, and failed list load show recoverable messages without technical identifiers.
4. At desktop and mobile widths, check the document list, purpose badges, upload dialog, keyboard focus, validation errors, and upload in-progress state. Confirm Work Order completion remains independent of document upload.
