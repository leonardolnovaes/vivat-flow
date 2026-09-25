# TSDT ERP instructions

- Read relevant `docs/` before architectural changes.
- Stay inside MVP scope unless explicitly instructed otherwise.
- Preserve modular-monolith boundaries; avoid microservices and unnecessary layers.
- Enforce authorization in the backend, use secure defaults, and never expose, log, or commit secrets.
- Write code, technical artifacts, internal logs, and developer documentation in English. Keep user-facing application content in Portuguese (Brazil), while structuring UI text for future localization.
- Do not add sensitive personal or medical data without an explicit requirement.
- Avoid broad unrelated refactors and unnecessary dependencies.
- Explain important architecture changes before implementing them.
- When code exists, run relevant tests.
- Do not commit unless explicitly requested.

## Development environment

- VS Code is the standard IDE/editor for this repository. Do not launch, invoke, depend on, or use Visual Studio or the Visual Studio Just-In-Time Debugger.
- Run .NET applications, tests, migrations, and tooling through the `dotnet` CLI.
- If a `dotnet` process crashes, capture and investigate the terminal exception or stack trace. Use VS Code-compatible debugging if needed; never attach or launch Visual Studio.
- Use `.\scripts\validate.ps1` for normal backend Release build, backend tests, frontend lint, and frontend production build. It uses restored NuGet assets without contacting package sources and keeps validation output separate from running APIs.
- If backend assets are missing or dependencies changed, explicitly run `.\scripts\restore.ps1` once, then rerun validation. Do not improvise raw `dotnet restore/build/test/run`, omit `--no-restore` or `--no-build`, or repeatedly retry a failing package source.

## Local application runtime

When the user asks to start or run the application, start the complete local environment and keep it running for manual browser validation. This includes, when required:

- Repository Docker Compose dependencies.
- PostgreSQL.
- Backend API.
- Frontend development server.

After startup:

- Verify that the API is healthy.
- Verify that the frontend is reachable.
- Provide the exact frontend, API, and health-check URLs to the user.
- Keep all processes running until the user explicitly asks to stop them.

Do not merely verify that the application can start and then terminate it. Do not expose passwords or other secrets; identify only intended local development accounts when applicable.

### Proactive startup after changes

After completing a meaningful functional change, proactively start or restart the application so the user can validate it in the browser.

Meaningful changes include:

- Frontend UI or UX changes.
- Authentication or authorization changes.
- New or modified application flows.
- API behavior changes.
- Backend changes that affect visible application behavior.
- Configuration or database changes that affect runtime behavior.

A proactive startup is not required for documentation-only changes, comments, formatting, or other changes that cannot affect runtime behavior.

If the application is already running:

- Reuse the existing environment when possible.
- Restart only the services necessary for the changes to take effect.
- Do not create duplicate application instances on arbitrary ports.
- Use `.\scripts\start-local.ps1` for backend/frontend startup (with `-BackendOnly`, `-FrontendOnly`, or `-Restart` when appropriate). It verifies the listener owner and `/health`, reuses a healthy TSDT process, and refuses an occupied port owned by another process. It starts hidden processes and writes logs under `.local\logs`.
- Never issue `dotnet run` or `npm run dev` directly against ports 7226 or 5173 without first using the startup script. A restart must stop the project-owned process and wait for its port to be released before starting a replacement.

After relevant changes, the final response should clearly state that the application is available and provide the URLs needed for manual validation.

Never stop the application automatically after validation. Stop it only when explicitly requested by the user.

### Docker-backed local workflow

- When Docker is running and the local application environment has been started, keep the localhost environment running unless the user explicitly asks to stop it.
- After meaningful application code changes, rebuild or update every affected local Docker image before runtime validation. When application services are defined in Docker Compose, use the updated images for the local validation environment.
- The current Compose configuration provides PostgreSQL only. Do not introduce application containers solely to satisfy this workflow; apply the image rebuild requirement to application images once they exist.
