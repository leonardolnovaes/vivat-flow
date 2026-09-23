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
dotnet restore backend\Tsdt.sln
dotnet run --project backend\src\Tsdt.Api --launch-profile https
dotnet test backend\Tsdt.sln
```

The API health endpoint is `GET /health`. Use the `https` launch profile: it listens on `https://localhost:7226` (and redirects the companion HTTP listener at `http://localhost:5273`). The default launch profile is also HTTPS so secure authentication and antiforgery cookies work during local development without relaxing their production-safe `CookieSecurePolicy.Always` setting. Development uses `SameSite=None; Secure` only because the Vite frontend is served on HTTP while the API is HTTPS; its explicit CORS allowlist and antiforgery token remain required. Non-development environments retain `SameSite=Lax; Secure`.

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

The app applies its Identity migration at startup. The configured administrator is created only when no users exist. For local frontend development, Vite uses `http://localhost:5173`; API CORS permits that origin only. The frontend default API address is `https://localhost:7226`.

Authentication endpoints are `GET /api/auth/csrf`, `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/change-password`, and `POST /api/auth/logout`; there is no public registration endpoint. The frontend fetches `/api/auth/csrf` before every state-changing request and sends its `token` response in `X-CSRF-TOKEN`, while retaining `credentials: 'include'` for the HttpOnly cookie.

Identity integration tests run with a relational SQLite database only under the `Testing` environment, covering cookies, antiforgery, bootstrap, and password changes without claiming PostgreSQL-specific behavior. PostgreSQL migration validation requires Docker Desktop or another reachable PostgreSQL instance.

User administration is available to `ADMIN` only at `/api/admin/users`. Bootstrap configuration also requires `BootstrapAdmin__FullName`.

### Module 1 isolated PostgreSQL validation

The following validation uses a disposable PostgreSQL container on port `55432` by default. It never connects to or removes the normal development database or its Docker volume. It generates its database and bootstrap credentials in memory and does not print them.

```powershell
dotnet build backend\Tsdt.sln --configuration Release
.\backend\tests\validate-module1-postgres.ps1
```

It verifies both Identity migrations (`20260922200327_CreateIdentityFoundation` and `20260922213000_AddUserAdministration`), the three application roles, initial ADMIN bootstrap, and bootstrap idempotency across an API restart. Supply unused ports when the defaults conflict:

```powershell
.\backend\tests\validate-module1-postgres.ps1 -PostgresPort 55433 -ApiPort 57227
```

## Frontend

```powershell
Set-Location frontend
npm install
npm run dev
```

Run `npm run build` for a production build and `npm run lint` for the configured frontend check.

### Module 1 stale-session browser validation

The Playwright acceptance test requires an active ADMIN whose password has already completed the mandatory password-change flow. Provide those existing local credentials only to the test process; do not store or print them:

```powershell
$env:E2E_ADMIN_EMAIL = $env:BootstrapAdmin__Email
$env:E2E_ADMIN_PASSWORD = $env:BootstrapAdmin__Password
npm run test:e2e -- module1-stale-session.spec.ts
Remove-Item Env:E2E_ADMIN_EMAIL, Env:E2E_ADMIN_PASSWORD
```

The mapping is appropriate only while the configured bootstrap password remains the ADMIN's effective password. Otherwise set `E2E_ADMIN_EMAIL` and `E2E_ADMIN_PASSWORD` from the approved local secret source for an active ADMIN. The test creates and deactivates its own timestamped USER and uses separate ADMIN and USER browser contexts.

## Local PostgreSQL

Create an ignored local environment file, review/change its local-only password, then start PostgreSQL:

```powershell
Copy-Item .env.example .env
docker compose -f infrastructure/docker-compose.yml up -d
```

Stop local infrastructure with:

```powershell
docker compose -f infrastructure/docker-compose.yml down
```

MinIO and database migrations are deferred until document storage and persistence are implemented.
