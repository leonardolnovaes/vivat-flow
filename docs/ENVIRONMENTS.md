# Runtime environments

This document is the canonical contract for Vivat Flow runtime environments. It distinguishes the code revision from the environment that runs it.

## DEV

DEV is the active, changeable development workspace.

- Frontend: `http://127.0.0.1:5175`
- API: `https://localhost:7227`
- Health: `https://localhost:7227/health`
- Database: `vivatflow_dev`
- Command: `./scripts/start-local.ps1`

DEV uses the current working tree and may change during development. It never changes DEMO processes or the `tsdt` database.

## DEMO

DEMO is the persistent client-facing environment. Its data is valuable and survives restarts and code deployments.

- Frontend: `http://127.0.0.1:5173`
- API: `https://localhost:7226`
- Health: `https://localhost:7226/health`
- Database: `tsdt`
- Command: `./scripts/start-demo.ps1 -Ref main`

`-Ref` resolves a committed revision. The script runs it from the persistent local worktree at `.local/demo/worktree`, rather than from the active DEV working tree. The deployed SHA is recorded at `.local/demo/deployed-sha.txt` only after health and unauthenticated-authentication checks succeed.

The DEMO command starts the existing PostgreSQL Compose service with `--no-recreate`, verifies that `tsdt` already exists, and fails closed if it does not. It never creates, truncates, resets, reseeds, or replaces that database. Bootstrap settings are forwarded only when already configured locally; the application bootstrapper is idempotent and does not overwrite existing users.

Before an update, the script compares source migration IDs with `__EFMigrationsHistory`. If any are pending, it creates a PostgreSQL custom-format backup under `.local/demo/backups` before the API starts and applies migrations. The API applies only its normal EF migrations; operators must review migrations with destructive or uncertain impact before explicitly deploying their revision to DEMO.

Use `./scripts/start-demo.ps1 -Stop` to stop only DEMO frontend/API processes. It leaves PostgreSQL, the persistent worktree, backups, and DEMO data intact.

## PREVIEW

PREVIEW is disposable and isolated. Create it only when a user explicitly asks for a preview, temporary environment, disposable environment, or isolated test copy.

Do not treat a request to start or expose DEMO as a request for PREVIEW. A preview must never silently replace or become the client-facing DEMO environment.

## Cloudflare

When exposing DEMO, Cloudflare always targets `http://127.0.0.1:5173`, whose Vite proxy targets the DEMO API on `7226`.

Use `./scripts/start-demo-tunnel.ps1` when a checked local named Cloudflare configuration exists. The script rejects a configuration unless it explicitly routes to the DEMO frontend target. Provide `-ConfigPath` for a non-default configuration.

If no named configuration exists, `./scripts/start-demo-tunnel.ps1 -QuickTunnel` is the explicit fallback. It creates a random, temporary `trycloudflare.com` URL without creating a database or preview. The link remains available only while the local `cloudflared` process runs. Use `./scripts/start-demo-tunnel.ps1 -Stop` to stop only the recorded DEMO tunnel.

Starting a tunnel is an explicit exposure action. It does not imply a DEMO code deployment, and a DEMO code deployment does not imply public exposure.

## Operating rules

"DEMO" describes a persistent runtime and its data. A branch name, `main`, or a SHA describes code to deploy. For example, `./scripts/start-demo.ps1 -Ref main` deploys the current committed `main` snapshot to DEMO while retaining the existing DEMO database.

Before changing DEMO, inspect the requested commit, deployed SHA, worktree status, expected ports, database availability, pending migrations, and tunnel configuration. Do not run DEMO from a dirty DEV working tree or on arbitrary ports.
