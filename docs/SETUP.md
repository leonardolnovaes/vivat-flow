# Development setup

Use this guide to prepare a workstation and repository for local development. For DEV, DEMO, and PREVIEW startup, restart, stop, exposure, and other runtime operations, follow the canonical [runtime environment contract](ENVIRONMENTS.md).

## Prerequisites

- Visual Studio Code
- .NET SDK 10.0.401 (pinned in `global.json`)
- Node.js 22.22.0 and npm 10.9.4
- Git
- Docker Desktop with Docker Compose

## Install dependencies

From the repository root in PowerShell, restore backend packages on first setup or after backend dependency changes:

```powershell
.\scripts\restore.ps1
```

Install frontend dependencies from the checked-in lockfile:

```powershell
Set-Location frontend
npm ci
Set-Location ..
```

## Prepare local configuration

Create an ignored local environment file and replace the example PostgreSQL password with a unique local value. Never commit credentials.

```powershell
Copy-Item .env.example .env
```

Start the local PostgreSQL dependency after Docker Desktop is running:

```powershell
docker compose --file .\infrastructure\docker-compose.yml up -d postgres
```

The [runtime environment contract](ENVIRONMENTS.md) defines which application environment may use this shared PostgreSQL service and how its databases are isolated. Follow it before starting an application service.

Trust the local ASP.NET Core HTTPS development certificate once if it is not already trusted:

```powershell
dotnet dev-certs https --trust
```

If a first local administrator is needed, set these process environment variables before the first API start. The administrator is created only when no users exist; keep the password out of files and logs.

```powershell
$env:BootstrapAdmin__Email = "admin@example.test"
$env:BootstrapAdmin__FullName = "Local Bootstrap Administrator"
$env:BootstrapAdmin__Password = "<unique-local-password>"
```

## Next steps

- Follow [ENVIRONMENTS.md](ENVIRONMENTS.md) to operate DEV, DEMO, or PREVIEW and to validate a running application.
- Follow the [repository change policy](../.ai-engineering/change-policy.md) for validation and test execution policy. Canonical `scripts/validate.ps1` runs only when the user explicitly requests it; normal AI-assisted development uses targeted unit tests and proportionate static, build, or lint checks. Non-unit suites are run manually or in CI/CD, never automatically by the AI.
- For manual non-unit test entry points, see `backend/tests/validate-module1-postgres.ps1`, `scripts/run-e2e.ps1`, and the integration test categories in the repository change policy.
