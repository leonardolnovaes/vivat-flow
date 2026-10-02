# Runtime environments

This document is the canonical contract for Vivat Flow runtime environments. It distinguishes the code revision from the environment that runs it.

## DEV

DEV is the active, changeable development workspace.

- Frontend: `http://127.0.0.1:5175`
- API: `https://localhost:7227`
- Health: `https://localhost:7227/health`
- Database: `vivatflow_dev`
- Command: `./scripts/start-local.ps1`

DEV uses the current working tree and may change during development. It never changes DEMO processes or the `tsdt` database. DEV and DEMO may share the PostgreSQL instance on port `5432`, but must never share a logical database. Normal local work uses DEV; promotion to DEMO is an explicit operation after QA through `./scripts/start-demo.ps1`. DEV is never exposed through Cloudflare.

### Local startup and manual validation

When asked to start or run the application, use `./scripts/start-local.ps1` to start the complete DEV environment for browser validation. Start the repository Docker Compose dependencies, PostgreSQL, backend API, and frontend development server as required. Verify that the API health endpoint responds and the frontend is reachable. Report the exact frontend, API, and health-check URLs. Identify only intended local development accounts when applicable; never expose passwords or other secrets.

Keep the environment running for manual validation. Do not stop it after checking startup or validation; stop processes only when the user explicitly asks. When Docker is running and the local application environment has been started, keep that localhost environment running until the user asks to stop it.

After a meaningful functional change, proactively start or restart DEV so the user can validate it in the browser. This includes frontend UI or UX changes, authentication or authorization changes, new or modified application flows, API behavior changes, backend changes affecting visible behavior, and configuration or database changes affecting runtime behavior. Documentation-only changes, comments, formatting, and other changes that cannot affect runtime behavior do not require proactive startup. After a runtime-relevant change, state in the final response that the application is available and provide the URLs needed for manual validation.

Reuse an already healthy environment when possible and restart only affected services. Use `./scripts/start-local.ps1` with `-BackendOnly`, `-FrontendOnly`, or `-Restart` as appropriate; do not create duplicate instances on arbitrary ports. The script checks the listener owner and `/health`, reuses a healthy TSDT process, refuses a port occupied by another process, starts hidden processes, and writes logs under `.local/logs`. A restart must stop the project-owned process and wait for its port to be released before starting a replacement.

After meaningful application code changes, rebuild or update every affected local Docker image before runtime validation. If application services are defined in Docker Compose, use those updated images for local validation. Compose currently provides PostgreSQL only; do not introduce application containers solely for this rule. Apply the application-image rebuild requirement when application images exist.

## DEMO

DEMO is the persistent client-facing environment. Its data is valuable and survives restarts and code deployments.

- Frontend: `http://127.0.0.1:5173`
- API: `https://localhost:7226`
- Health: `https://localhost:7226/health`
- Database: `tsdt`
- Command: `./scripts/start-demo.ps1 -Ref main`

Never run `dotnet run` or `npm run dev` directly against DEMO ports `7226` or `5173` without first using `./scripts/start-demo.ps1`.

`-Ref` resolves a committed revision. For the canonical `-Ref main` command, the script refreshes and deploys `origin/main`; other refs resolve locally as explicitly supplied. The script runs the revision from the persistent local worktree at `.local/demo/worktree`, rather than from the active DEV working tree. When the revision changes, it stops the owned DEMO frontend/API before changing that worktree. The deployed SHA is recorded at `.local/demo/deployed-sha.txt` only after both services and the unauthenticated-authentication check succeed.

The DEMO command starts the existing PostgreSQL Compose service with `--no-recreate`, verifies that `tsdt` already exists, and fails closed if it does not. It never creates, truncates, resets, reseeds, or replaces that database. Bootstrap settings are forwarded only when already configured locally; the application bootstrapper is idempotent and does not overwrite existing users.

Before an update, the script compares source migration IDs with `__EFMigrationsHistory`. If any are pending, it creates a PostgreSQL custom-format backup under `.local/demo/backups` before the API starts and applies migrations. The API applies only its normal EF migrations; operators must review migrations with destructive or uncertain impact before explicitly deploying their revision to DEMO.

Use `./scripts/start-demo.ps1 -Stop` to stop only DEMO frontend/API processes. It leaves PostgreSQL, the persistent worktree, backups, and DEMO data intact. `-BackendOnly` and `-FrontendOnly` are maintenance operations allowed only when both the requested SHA and the DEMO worktree already match the recorded full deployment; any divergence requires a full-stack deployment. Dependency restoration runs only before a changed full deployment, after its owned processes have stopped.

## PREVIEW

PREVIEW is disposable and isolated. Create it only when a user explicitly asks for a preview, temporary environment, disposable environment, or isolated test copy.

Do not treat a request to start or expose DEMO as a request for PREVIEW. A preview must never silently replace or become the client-facing DEMO environment.

## Cloudflare

When exposing DEMO, Cloudflare always targets `http://127.0.0.1:5173`, whose Vite proxy targets the DEMO API on `7226`.

Use `./scripts/start-demo-tunnel.ps1` when a checked local named Cloudflare configuration exists. The script rejects a configuration unless it explicitly routes to the DEMO frontend target and sets `httpHostHeader: 127.0.0.1`; the host override keeps Vite's allowlist deterministic for a custom public hostname. Provide `-ConfigPath` for a non-default configuration.

If no named configuration exists, `./scripts/start-demo-tunnel.ps1 -QuickTunnel` is the explicit fallback. It creates a random, temporary `trycloudflare.com` URL without creating a database or preview. The link remains available only while the local `cloudflared` process runs. Use `./scripts/start-demo-tunnel.ps1 -Stop` to stop only the recorded DEMO tunnel.

Starting a tunnel is an explicit exposure action. It does not imply a DEMO code deployment, and a DEMO code deployment does not imply public exposure.

## Operating rules

"DEMO" describes a persistent runtime and its data. A branch name, `main`, or a SHA describes code to deploy. For example, `./scripts/start-demo.ps1 -Ref main` deploys the current committed `main` snapshot to DEMO while retaining the existing DEMO database.

Before changing DEMO, inspect the requested commit, deployed SHA, worktree status, expected ports, database availability, pending migrations, and tunnel configuration. Do not run DEMO from a dirty DEV working tree or on arbitrary ports.
