# Vivat Flow frontend

React + TypeScript + Vite frontend for Vivat Flow.

## Standard local workflow

Use the repository-level DEV runner rather than launching an ad-hoc Vite instance:

```powershell
.\scripts\start-local.ps1 -FrontendOnly
```

Standard DEV frontend:

`http://127.0.0.1:5175`

The frontend uses relative API routes and the DEV proxy targets the Vivat Flow API at `https://localhost:7227`.

## Quality rules

All meaningful frontend work must follow:

- [Frontend standards](../docs/FRONTEND_STANDARDS.md)
- [Repository/agent workflow](../AGENTS.md)
- [Development setup](../docs/SETUP.md)

User-facing content is pt-BR. Technical identifiers and source code are English.

For proportional frontend validation, use the relevant lint/build checks. Playwright/E2E is manual or CI-only unless the user explicitly authorizes execution.

Do not run `scripts/validate.ps1` automatically; canonical validation is user-requested only.
