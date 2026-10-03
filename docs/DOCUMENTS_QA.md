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

1. Open an active Customer as ADMIN. Confirm the Documents section loads, the empty state has only guidance and one upload action in the section header, and pagination keeps newest documents first. Upload one PDF with purpose `Entrega ao cliente`, a category, description, and each available context. Confirm the success message, refreshed list, readable context and uploader, and protected download with the original filename.
2. Open the same Customer as MANAGER. Confirm Quote/Contract contexts and Contract/SignedDocument categories are absent. Confirm existing commercial documents are absent, while permitted documents can be uploaded and downloaded.
3. Open an active Customer as USER assigned to one Work Order. Confirm only noncommercial documents for that Work Order appear, with no upload action. Confirm an unassigned USER and an inactive Customer do not expose document metadata or counts. Confirm an expired session, denied request, missing document, failed download, failed upload, and failed list load show recoverable messages without technical identifiers.
4. At desktop and mobile widths, check the document list, purpose badges, upload dialog, keyboard focus, validation errors, and upload in-progress state. Confirm Work Order completion remains independent of document upload.
5. In DEV, make only the context-choice request fail while the upload endpoint remains available. Confirm the message refers only to unavailable links, the selector stays unavailable, the entered file/category/purpose/description remain intact, and submitting without context succeeds and refreshes the Customer list. A 401 must still expire the session; only a 403 may say the role cannot consult links.
6. For an ADMIN Customer with two Contracts formalized from the same Quote at different times, confirm the upload choices and document list identify them as distinct numbered Contracts under that Quote. Verify the selected Contract remains the document's origin after upload and download.
