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
.\scripts\validate.ps1 # Normal build, tests, lint, and production build.
.\scripts\start-local.ps1 -BackendOnly
```

### Safe local startup and restart

The canonical local ports are frontend `5173`, backend HTTPS `7226`, and PostgreSQL `5432`. They are fixed rather than dynamically selected. Always use `scripts\start-local.ps1` to start the local API and frontend. It checks ports 7226 and 5173, inspects the listening command to confirm it belongs to TSDT, calls `/health`, and reuses a healthy instance. It refuses to start over an unknown process or an unhealthy TSDT process unless restart was explicitly requested. Backend and frontend run hidden, with stdout and stderr under `.local\logs`.

```powershell
# Reuse healthy local services, or start only services that are absent.
.\scripts\start-local.ps1

# Start or reuse one service.
.\scripts\start-local.ps1 -BackendOnly
.\scripts\start-local.ps1 -FrontendOnly

# Replace project-owned services after relevant code/configuration changes.
.\scripts\start-local.ps1 -Restart
```

Do not invoke `dotnet run` or `npm run dev` directly for the standard ports. During a restart, the script stops the project-owned listener, waits for the port to be released, and starts one replacement. Normal validation uses `scripts\validate.ps1`: its Release output is isolated from running APIs, so validation leaves healthy services running. If restored assets are missing, run `scripts\restore.ps1` explicitly once; it does not disable package signature checks.

The API health endpoint is `GET /health`. Use the `https` launch profile: it listens only on `https://localhost:7226`. The default launch profile is also HTTPS so secure authentication and antiforgery cookies work during local development without relaxing their production-safe `CookieSecurePolicy.Always` setting. Development uses `SameSite=None; Secure` only because the Vite frontend is served on HTTP while the API is HTTPS; its explicit CORS allowlist and antiforgery token remain required. Non-development environments retain `SameSite=Lax; Secure`.

Trust the local ASP.NET Core development certificate once on the workstation if it is not already trusted:

```powershell
dotnet dev-certs https --trust
```

Before the first API start, set environment variables (never commit their values):

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=tsdt;Username=tsdt;Password=<local-password>"
$env:BootstrapAdmin__Email = "admin@example.test"
$env:BootstrapAdmin__FullName = "Local Bootstrap Administrator"
$env:BootstrapAdmin__Password = "<unique-local-password>"
```

The app applies its Identity migration at startup. The configured administrator is created only when no users exist. For local frontend development, Vite uses the canonical `http://127.0.0.1:5173` and proxies `/api` and `/health` to `https://localhost:7226`. The frontend uses same-origin relative API routes by default. The API CORS allowlist remains limited to that direct local frontend origin for cases that bypass the proxy.

Authentication endpoints are `GET /api/auth/csrf`, `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/change-password`, and `POST /api/auth/logout`; there is no public registration endpoint. The frontend fetches `/api/auth/csrf` before every state-changing request and sends its `token` response in `X-CSRF-TOKEN`, while retaining `credentials: 'include'` for the HttpOnly cookie.

Identity integration tests run with a relational SQLite database only under the `Testing` environment, covering cookies, antiforgery, bootstrap, and password changes without claiming PostgreSQL-specific behavior. PostgreSQL migration validation requires Docker Desktop or another reachable PostgreSQL instance.

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

`scripts\validate.ps1` runs both the frontend production build and lint check.

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

Any future cleanup tool is limited to Development or Test, verifies the target database/environment identity before it changes data, and deletes only records that carry the current clearly identifiable E2E run prefix. It must fail closed when that verification is unavailable. The domain model must not add an `IsTestData` field for this purpose.

## Local PostgreSQL

Create an ignored local environment file, review/change its local-only password, then start PostgreSQL:

```powershell
Copy-Item .env.example .env
docker compose -f infrastructure/docker-compose.yml up -d
```

Compose binds PostgreSQL only to the canonical local address `127.0.0.1:5432`; `POSTGRES_PORT` is not configurable for the standard environment.

Stop local infrastructure with:

```powershell
docker compose -f infrastructure/docker-compose.yml down
```

MinIO remains deferred until document storage is implemented. The API applies the existing Identity and Customers migrations at startup.
