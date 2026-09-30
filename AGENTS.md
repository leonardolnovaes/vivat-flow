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
- Follow the Git workflow and review gate below for branch, commit, push, pull request, review, and merge behavior.
- All new frontend screens and meaningful frontend changes must follow `docs/FRONTEND_STANDARDS.md`; UI work is not complete until its running-screen visual/manual QA gate passes.

## Git workflow and review gate

`main` is the stable integration branch. Agents must not implement meaningful changes directly on `main`, must not push feature work to `main`, and must never merge a pull request. The user is always the final reviewer and performs the merge manually.

### Branch creation

- Every task that will modify repository files must begin by creating a dedicated short-lived branch **before the first edit**.
- A new implementation task must start from the current updated `main`; do not begin new implementation work on a branch left over from a previous task.
- The only exception is a correction requested during review of an already-open pull request: review fixes must stay on that same branch and pull request.
- Never implement directly on `main`.
- Create the task branch using:
  - `feat/<short-name>` for new behavior.
  - `fix/<short-name>` for defect corrections.
  - `refactor/<short-name>` for behavior-preserving refactors.
  - `chore/<short-name>` for tooling, infrastructure, repository configuration, or maintenance.
  - `docs/<short-name>` for documentation-only changes.
  - `test/<short-name>` for test-only changes.
- Do not create `develop`, release branches, or additional workflow branches unless the user explicitly asks for them.
- Before editing, inspect the current branch and working tree. Do not discard, rewrite, stage, or commit unrelated pre-existing changes. If unrelated changes cannot be safely isolated, stop and report the conflict.

### When to commit

- Do not create a commit for every small edit.
- Commit when the requested scope has reached a cohesive, reviewable checkpoint and the applicable automated validation for that checkpoint has passed.
- For the final implementation commit, the requested scope must be complete, known blocking failures must be resolved, and the required repository validation for the change must have passed or be explicitly documented as not applicable.
- Do not commit knowingly broken or incomplete implementation only to create a checkpoint unless the user explicitly asks for a checkpoint commit.
- Stage only files that belong to the current task.
- Use concise Conventional Commit-style messages such as `feat: ...`, `fix: ...`, `refactor: ...`, `chore: ...`, `docs: ...`, or `test: ...`.

### Mandatory completion handoff: commit, push, and pull request

For every prompt/task that changes repository files, completion is not finished at the working-tree stage. When the requested scope is complete, the agent must automatically:

1. Review the working tree and ensure only intended task changes are included.
2. Run the validation permitted/required for that task.
3. Commit the completed scope on the task branch.
4. Push that branch to `origin`.
5. Open a pull request targeting `main`.
6. Return the branch name, latest commit SHA, pull request number, and pull request URL.

Do not leave completed implementation only in the local working tree and wait for the user to ask for commit, push, or pull request creation.

Do not push incomplete experimental work unless the user explicitly asks to preserve or share it remotely.

### When to open a pull request

- Every branch whose changes are intended to enter `main` must go through a pull request.
- Opening the pull request is mandatory at the end of every completed repository-changing task; it is the handoff point for ChatGPT code review, not permission to merge.
- Base the pull request on `main`.
- Do not open a pull request for investigation/read-only work with no repository change, abandoned experiments, or intentionally incomplete work unless the user explicitly asks for a draft pull request.
- Manual UI/E2E/integration QA may still be pending when the pull request is opened. Mark it clearly as pending.
- Use `.github/pull_request_template.md` and keep the pull request focused on one task.
- After ChatGPT review feedback, keep using the same branch and the same pull request. Apply requested corrections, validate the affected scope, commit the correction, push the same branch, and return the new commit SHA and existing pull request number for re-review.
- Never create a second pull request only to address review findings.

### Required handoff after opening or updating a pull request

Report all of the following to the user:

- Pull request number and URL.
- Branch name.
- Latest commit SHA.
- Concise scope summary.
- Automated validation executed, including canonical validation count, unit-test execution count, non-unit execution count, and retries.
- Manual QA still required, with exact steps or commands when applicable.
- Known risks, limitations, or intentionally deferred findings.

Then stop. Do not merge the pull request.

### Review and merge gate

- A pull request is not ready for `main` merely because implementation and automated validation succeeded.
- The mandatory gate is: new task branch created before editing -> implementation complete -> commit -> push -> pull request opened -> ChatGPT code review -> requested corrections resolved on the same branch/PR -> user manual QA when applicable -> user final review -> user performs the merge manually.
- Treat review findings as:
  - `BLOCKER`: must be fixed before merge.
  - `NON-BLOCKING`: valid improvement that does not block the current delivery.
  - `CLEAN`: no relevant issue found in the reviewed scope.
- If review returns a blocker, fix it on the same branch, commit, push, and report the new commit SHA and the existing pull request number for re-review.
- Agents must not approve their own work as a substitute for the designated review gate.
- Agents must never merge, squash-merge, rebase-merge, close, or otherwise finalize a pull request. This remains true even if the user says the change is approved or ready: approval means the agent must stop and hand control back to the user, who performs the merge manually.

## Reuse-first engineering

TSDT ERP is the repository source of truth and the reference implementation for future projects. Build clean, reusable technical patterns with minimal business coupling, but do not prematurely create a generic framework or separate shared library.

- Before creating code, inspect the repository and identify the closest reference implementation.
- Reuse or extend established components, helpers, services, UI patterns, validation, API and authorization conventions, tests, infrastructure, scripts, and documentation whenever appropriate; do not introduce a parallel implementation for behavior already solved elsewhere.
- Decision order: reuse an existing implementation directly; evolve it with a small extension; extract a reusable abstraction only when duplication already exists or the pattern will clearly be reused; otherwise create a new implementation consistent with repository standards.
- Do not copy-paste repeated behavior across feature files. Prefer shared technical primitives with clear ownership, while keeping feature-specific domain configuration within its module.
- When a shared implementation is insufficient, understand its consumers, improve the shared foundation safely, preserve compatibility when appropriate, migrate affected consumers, and validate regressions.
- For every substantial development task, before coding, identify the reference implementation, what will be reused unchanged, what will be extended, and what genuinely must be created. State explicitly when repository inspection finds no suitable pattern.
- During review and in the final report, explicitly state what existing code was reused, what was extended, and what genuinely had to be created. Check for avoidable duplication in components, helpers, validation, styles, API/error handling, authorization, and test setup.
- For frontend work, also follow `docs/FRONTEND_STANDARDS.md`.

## Execution and validation discipline

- During normal AI-assisted development, automatically execute only unit tests. Static checks, compilation/build, and linting remain part of normal validation.
- The AI may create, modify, refactor, and review E2E, integration, smoke, regression, performance, and other non-unit tests, but must not execute them automatically. Creating or changing such a test does not grant execution permission.
- Non-unit suites are run separately by a developer/team or dedicated CI/CD pipeline. When the AI changes a non-unit test, report its coverage, the manual command to run it, and explicitly that it was not executed.
- Classify backend tests with xUnit `Trait("Category", "Unit")` or `Trait("Category", "Integration")`. Tests without a `Unit` category are excluded from the AI's default validation command.
- Before editing, inspect only the files needed to resolve the task, reuse established repository patterns, and identify the exact files expected to change.
- Do not perform open-ended repository hardening, opportunistic refactoring, or unrelated improvements. Report out-of-scope findings instead.
- During implementation, use the smallest permitted validation that proves the current change. Do not run non-unit suites automatically.
- Prefer a single blocking command with an appropriate timeout over polling a running process. When a command already blocks until completion, wait for its result; do not poll logs or process state with `Start-Sleep`, `Get-Content`, `Get-Process`, or repeated status commands. Inspect logs only after completion or when investigating a genuine timeout or failure.
- When validation fails, diagnose the specific failure before rerunning it. Do not rerun an unchanged failing command unless the failure is known to be transient, and do not escalate a targeted failure to repository-wide testing without cause.
- Run the canonical `./scripts/validate.ps1` validation once after implementation is stable. It runs only unit tests, build, and lint. PostgreSQL and Playwright validation are manual/CI responsibilities unless the user explicitly directs their execution.
- Stop once the requested implementation and required acceptance gates pass. Report optional findings or remaining risks without beginning another review, hardening, cleanup, optimization, or validation cycle.

### Cost-aware validation / fast feedback

The default AI validation proceeds through permitted checks only:

cheap/static checks -> targeted unit tests -> canonical validation once.

#### 1. Static/preflight first

Before running builds or unit tests:

- Inspect only the changed/relevant files.
- Run targeted `rg`/static checks when useful.
- For changed non-unit tests, review locator uniqueness, labels, async/loading states, and obvious assertions statically before handing them off.

Do not launch an expensive test runner to discover basic spelling, locator, or selector problems that can be found statically.

#### 2. Canonical validation is a gate, not a development loop

The canonical repository validation is `./scripts/validate.ps1`.

Use targeted checks during implementation. Run the canonical validation when the implementation is believed complete. Do not repeatedly execute `validate.ps1` between small edits.

#### 3. After canonical validation passes

If `validate.ps1` has already passed and subsequent changes affect only non-unit test files, selectors, assertions, test synchronization, or test-only helpers, do not rerun it. Report the affected manual/CI command instead.

If production frontend code changes, run the minimum relevant frontend lint/build check. If backend production code or contracts change, run the relevant backend tests before the final canonical gate.

#### 4. Batch fixes

After any failed automated test:

- Inspect the complete error output first.
- Distinguish application, test, and environment defects.
- Identify related issues.
- Apply the complete reasonable correction.
- Only then rerun the minimum affected scope.

#### 5. Execution accounting

Engineering summaries for substantial changes must report:

- Canonical validation execution count.
- Unit-test execution count.
- Non-unit test execution count (normally zero; if non-zero, record the user's explicit authorization).
- Retries and their cause.

This allows the project to identify validation waste.

## Development environment

- VS Code is the standard IDE/editor for this repository. Do not launch, invoke, depend on, or use Visual Studio or the Visual Studio Just-In-Time Debugger.
- Run .NET applications, tests, migrations, and tooling through the `dotnet` CLI.
- If a `dotnet` process crashes, capture and investigate the terminal exception or stack trace. Use VS Code-compatible debugging if needed; never attach or launch Visual Studio.
- Use `.\scripts\validate.ps1` for normal backend Release build, backend tests, frontend lint, and frontend production build. It uses restored NuGet assets without contacting package sources and keeps validation output separate from running APIs.
- If backend assets are missing or dependencies changed, explicitly run `.\scripts\restore.ps1` once, then rerun validation. Do not improvise raw `dotnet restore/build/test/run`, omit `--no-restore` or `--no-build`, or repeatedly retry a failing package source.

## Local application runtime

### Permanent DEMO / DEV boundary

The customer-facing Cloudflare environment is DEMO. Normal local work is DEV only: use `scripts\start-local.ps1`, which owns frontend `5175`, API `7227`, and the separate `vivatflow_dev` logical database on the shared PostgreSQL port `5432`. Never restart, migrate, reset, or otherwise change DEMO during ordinary development. DEV and DEMO may share the PostgreSQL instance, but never the same database. Promotion to DEMO is an explicit operation after QA; DEV is never exposed through Cloudflare.

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
