# Development setup

## Prerequisites

- Visual Studio Code
- .NET SDK 10.0.401 (pinned in `global.json`)
- Node.js 22.22.0 and npm 10.9.4
- Git
- Docker Desktop with Docker Compose

## Backend

From the repository root in PowerShell:

```powershell
.\scripts\restore.ps1  # Only on first setup or after dependency changes.
.\scripts\validate.ps1 # Normal build, unit tests, lint, and production build.
.\scripts\start-local.ps1 -BackendOnly
```

### Environment boundary

The authoritative DEV, DEMO, PREVIEW, migration-protection, and Cloudflare rules are in [ENVIRONMENTS.md](ENVIRONMENTS.md). Normal development uses DEV only; DEMO changes require an explicit deployment action through `scripts/start-demo.ps1` after QA.

DEV and DEMO share PostgreSQL on `127.0.0.1:5432`, but never the same logical database. DEV uses `vivatflow_dev`; DEMO retains the persistent `tsdt` database. Port `5174` remains reserved by the isolated E2E workflow, so DEV uses frontend port `5175` and API port `7227`.

Always use `scripts\start-local.ps1` to operate DEV. It only examines and controls the DEV API/frontend ports, checks the exact DEV command before reuse or termination, verifies the shared PostgreSQL service, and creates only `vivatflow_dev` if absent. It writes logs below `.local\logs\dev`. A listener on a DEV port that is not the expected DEV command is refused, never terminated.

```powershell
# Reuse healthy DEV services, or start only DEV services that are absent.
.\scripts\start-local.ps1

# Start or reuse one service.
.\scripts\start-local.ps1 -BackendOnly
.\scripts\start-local.ps1 -FrontendOnly

# Replace only project-owned DEV services after relevant code/configuration changes.
.\scripts\start-local.ps1 -Restart

# Stop only DEV frontend and API. Shared PostgreSQL remains running for DEMO.
.\scripts\start-local.ps1 -Stop
```

Do not invoke `dotnet run` or `npm run dev` directly for DEV or DEMO ports. The startup scripts validate process ownership before reuse or termination. `scripts\validate.ps1` is available only for explicit user-requested canonical validation; it is not part of the normal automatic AI workflow. Targeted unit tests plus proportional build/lint/static checks are the default. The validation runner's Release output is isolated from running APIs, so an explicitly requested canonical run leaves healthy services running. If restored assets are missing, run `scripts\restore.ps1` explicitly once; it does not disable package signature checks.

The API health endpoint is `GET /health`; DEV is available at `https://localhost:7227` and `https://localhost:7227/health`. The DEV frontend is `http://127.0.0.1:5175`. The script starts the backend with its Development environment, DEV URL, DEV database connection, and the DEV frontend CORS origin without changing the historical launch profile or Vite defaults used by DEMO. Development uses `SameSite=None; Secure` only because the Vite frontend is served on HTTP while the API is HTTPS; its explicit CORS allowlist and antiforgery token remain required. Non-development environments retain `SameSite=Lax; Secure`.

Trust the local ASP.NET Core development certificate once on the workstation if it is not already trusted:

```powershell
dotnet dev-certs https --trust
```

Before the first API start, set environment variables (never commit their values):

```powershell
$env:BootstrapAdmin__Email = "admin@example.test"
$env:BootstrapAdmin__FullName = "Local Bootstrap Administrator"
$env:BootstrapAdmin__Password = "<unique-local-password>"
```

The app applies its Identity migration at DEV startup only, against `vivatflow_dev`. The configured administrator is created only when no users exist. DEV Vite proxies `/api` and `/health` to `https://localhost:7227`; the frontend uses same-origin relative API routes by default. The API CORS allowlist is injected only for `http://127.0.0.1:5175` in the DEV process.

Authentication endpoints are `GET /api/auth/csrf`, `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/change-password`, and `POST /api/auth/logout`; there is no public registration endpoint. The frontend fetches `/api/auth/csrf` before every state-changing request and sends its `token` response in `X-CSRF-TOKEN`, while retaining `credentials: 'include'` for the HttpOnly cookie.

Identity integration tests run with a relational SQLite database only under the `Testing` environment, covering cookies, antiforgery, bootstrap, and password changes without claiming PostgreSQL-specific behavior. PostgreSQL migration validation requires Docker Desktop or another reachable PostgreSQL instance. The default AI validation excludes both categories: it runs only xUnit tests tagged `Category=Unit`.

User administration is available to `ADMIN` only at `/api/admin/users`. Bootstrap configuration also requires `BootstrapAdmin__FullName`.

### Module 1 isolated PostgreSQL validation

The following validation uses a disposable PostgreSQL container on port `55432` by default. It never connects to or removes the normal development database or its Docker volume. It generates its database and bootstrap credentials in memory and does not print them.

```powershell
.\scripts\validate.ps1
.\backend\tests\validate-module1-postgres.ps1
```

It verifies the Identity migrations (`20260922200327_CreateIdentityFoundation`, `20260922213000_AddUserAdministration`, and `20260923010000_HardenUserProfileValidation`), the Customers migration, the three application roles, initial ADMIN bootstrap, and bootstrap idempotency across an API restart. Supply unused ports when the defaults conflict:

```powershell
.\backend\tests\validate-module1-postgres.ps1 -PostgresPort 55433 -ApiPort 57227
```

## Frontend

```powershell
Set-Location frontend
npm install
Set-Location ..
.\scripts\start-local.ps1 -FrontendOnly
```

`scripts\validate.ps1`, when the user explicitly requests canonical validation, runs the backend Release build, only xUnit tests tagged `Category=Unit`, and the frontend production build and lint check. It deliberately excludes integration, PostgreSQL, Playwright/E2E, smoke, regression, and performance suites.

### Non-unit test execution

Non-unit suites are not run automatically by the AI, including after those tests are created or changed. Run them manually or in CI/CD. For example:

```powershell
dotnet test .\backend\Tsdt.sln --configuration Release --filter 'Category=Integration'
.\backend\tests\validate-module1-postgres.ps1
.\scripts\run-e2e.ps1
```

When changing a non-unit suite, record what it covers, its manual command, and that it was not executed.

### Module 1 stale-session browser validation

The Playwright acceptance test requires an active ADMIN whose password has already completed the mandatory password-change flow. Provide those existing local credentials only to the test process; do not store or print them:

```powershell
$env:E2E_ADMIN_EMAIL = $env:BootstrapAdmin__Email
$env:E2E_ADMIN_PASSWORD = $env:BootstrapAdmin__Password
npm run test:e2e -- module1-stale-session.spec.ts
Remove-Item Env:E2E_ADMIN_EMAIL, Env:E2E_ADMIN_PASSWORD
```

The mapping is appropriate only while the configured bootstrap password remains the ADMIN's effective password. Otherwise set `E2E_ADMIN_EMAIL` and `E2E_ADMIN_PASSWORD` from the approved local secret source for an active ADMIN. The test creates and deactivates its own timestamped USER and uses separate ADMIN and USER browser contexts.

### E2E data isolation

Run the permanent Customers browser suite only through the isolated runner:

```powershell
.\scripts\run-e2e.ps1
```

It starts a uniquely named, disposable PostgreSQL container on port `55435` by default, applies migrations from zero, generates an in-memory bootstrap ADMIN credential, runs an isolated API on `57228` and Vite proxy on `5174`, then removes all three processes and the container. It does not read `.env`, reuse `5432`, print credentials, or issue any database cleanup command. Override ports only with unused values. The suite bootstraps MANAGER and USER through the real ADMIN UI and completes their mandatory password changes.

Each run starts from a known state and carries a unique run identifier; records created by the suite use names such as `E2E-{runId}-Customer`.

### Fast E2E development loop

For fast local feedback, provision one isolated E2E environment and keep it after a focused test run:

```powershell
.\scripts\run-e2e.ps1 -KeepEnvironment -PlaywrightArgs 'module1-authentication.spec.ts'
```

Rerun an affected spec against that same verified E2E database, API, frontend, and authenticated test state without rebuilding or reprovisioning:

```powershell
.\scripts\run-e2e.ps1 -ReuseEnvironment -PlaywrightArgs 'module1-authentication.spec.ts'
```

When finished, remove only the recorded E2E-owned processes and PostgreSQL container:

```powershell
.\scripts\run-e2e.ps1 -Cleanup
```

Reuse verifies recorded process ownership, container labels, database identity, health endpoints, and a production-source fingerprint before each run. It fails closed if the environment is stale or production source has changed; clean it and provision again in that case. Playwright test-source changes can be rerun against the reusable environment. The default command, `./scripts/run-e2e.ps1`, remains the clean isolated acceptance command for manual or CI execution and always provisions and cleans up its own environment.

Any future cleanup tool is limited to Development or Test, verifies the target database/environment identity before it changes data, and deletes only records that carry the current clearly identifiable E2E run prefix. It must fail closed when that verification is unavailable. The domain model must not add an `IsTestData` field for this purpose.

## Local PostgreSQL

Create an ignored local environment file, review/change its local-only password, then start PostgreSQL:

```powershell
Copy-Item .env.example .env
.\scripts\start-local.ps1 -BackendOnly
```

The existing `infrastructure/docker-compose.yml` supplies the shared PostgreSQL instance on `127.0.0.1:5432`. DEV does not start, stop, recreate, or reset that container. When the shared service is available, `scripts\start-local.ps1` safely checks `pg_database` and creates only `vivatflow_dev` if absent. It intentionally does not use `POSTGRES_DB` or `ConnectionStrings__DefaultConnection` for DEV identity.

Stop DEV application processes with:

```powershell
.\scripts\start-local.ps1 -Stop
```

MinIO remains deferred until document storage is implemented. The API applies pending application migrations at DEV startup against `vivatflow_dev`.
