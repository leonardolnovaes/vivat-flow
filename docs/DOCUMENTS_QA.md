# Documents foundation manual QA

Status: backend foundation ready for code review. Manual authenticated API and storage QA remains pending. Do not use DEMO for this QA.

## API and policy

- `POST /api/customers/{customerId}/documents`: ADMIN or MANAGER only. Send multipart `file`, `category`, optional `description`, and optional `contextType` plus `contextId`. Both context fields must be present together. The CSRF header is required.
- `GET /api/customers/{customerId}/documents?page=1&pageSize=25`: ADMIN, MANAGER, or USER in the tenant; page size is capped at 100.
- `GET /api/documents/{id}/download`: ADMIN, MANAGER, or USER in the tenant. Response is an attachment with a sanitized original filename and no internal storage key.

Categories: `General`, `Report`, `Certificate`, `Contract`, `Evidence`, `Photo`, `SignedDocument`, `Other`. Accepted files in this phase: PDF, PNG, JPEG, and WebP with matching extension, MIME type, and signature. Default maximum size is 10 MiB, configurable through `DocumentStorage:MaxFileSizeBytes`. `DocumentStorage:RootPath` selects the private local root; the default is `.local/documents` under the API content root. Back up this root together with PostgreSQL metadata. No cloud storage or permanent removal is implemented.

## Manual checks in DEV

1. As ADMIN/MANAGER, upload a small PDF to an existing Customer without a context. Confirm 201 response, metadata list entry, download filename and bytes, and a `DOCUMENT_UPLOADED` audit record with actor, tenant, customer, and time.
2. Upload documents with Customer, Customer Unit, Quote, Contract, and Work Order contexts. Confirm each context belongs to the route Customer and tenant. Upload to a completed Work Order and verify its status does not change.
3. Attempt another Customer's context and another tenant's Customer/context/document ID. Confirm no foreign metadata or file is disclosed. Test unauthenticated access and USER upload denial; confirm authorized USER listing/download follows the tenant policy.
4. Try an empty file, a file above the configured limit, an executable, a PDF renamed as an image, and a filename containing directory separators. Confirm safe validation messages and no physical path in any response.
5. Confirm the private storage root contains only generated Organization/key names and is not served as a public web directory. Simulate storage and database failures in an isolated DEV test environment and confirm no metadata row or orphaned file remains.

The targeted `Category=Unit&FullyQualifiedName~Tsdt.Tests.Documents` tests cover the service and local provider. No E2E, integration, smoke, or performance suite was executed by the agent.
