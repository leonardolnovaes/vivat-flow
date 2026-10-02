# Vivat Flow repository instructions

- Read relevant `docs/` before architectural changes.
- Stay inside MVP scope unless explicitly instructed otherwise.
- Preserve modular-monolith boundaries; avoid microservices and unnecessary layers.
- Enforce authorization in the backend, use secure defaults, and never expose, log, or commit secrets.
- Write code, technical artifacts, internal logs, and developer documentation in English. Keep user-facing application content in Portuguese (Brazil), while structuring UI text for future localization.
- Do not add sensitive personal or medical data without an explicit requirement.
- Avoid broad unrelated refactors and unnecessary dependencies.
- Explain important architecture changes before implementing them.
- When repository code changes, run only the validation allowed by the `Execution and validation discipline` section below; do not broaden test scope beyond those rules.
- Follow the Git workflow and review gate below for branch, commit, push, pull request, review, and merge behavior.
- All new frontend screens and meaningful frontend changes must follow `docs/FRONTEND_STANDARDS.md`; UI work is not complete until its running-screen visual/manual QA gate passes.

## AI Engineering adoption

Vivat Flow adopts the AI Engineering operating model pinned in `.ai-engineering/adoption.yml`. The external framework defines reusable mechanisms and evidence discipline; this repository owns the operational policy.

- Existing Vivat Flow engineering, validation, Git, security, QA, and runtime rules remain authoritative until each rule is evaluated individually. Do not assume an existing rule is cheaper, more expensive, or obsolete merely because it already exists.
- For meaningful repository changes, the implementing agent performs a pre-handoff self-review of the final diff. Self-review does not count as independent review.
- Independent review is cross-agent by default:
  - Codex implementation -> ChatGPT independent review.
  - ChatGPT implementation -> Codex independent review.
  - Another implementer -> an independent reviewer selected by the user/project policy.
- If the designated independent reviewer is unavailable, mark independent review as pending. Do not silently substitute the implementer's self-review.
- The independent reviewer must perform both the normal technical review and the AI Engineering efficiency review, separating measured observations from inference.
- This cross-agent topology is `EXPERIMENTAL`: measure its review escapes, retries, validation cost, and useful findings. Revisit it when evidence or tooling changes; do not treat it as permanent.
- The user remains the final decision-maker and performs the merge manually.
- AI Engineering findings use the smallest justified action: `NONE`, `WATCH`, `EXPERIMENT`, `PROMOTE`, or `REJECT`. Do not create framework changes from isolated findings unless evidence justifies them.

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
6. Identify the implementing agent and the designated independent reviewer under the local AI Engineering policy.
7. Return the branch name, latest commit SHA, pull request number, and pull request URL.

Do not leave completed implementation only in the local working tree and wait for the user to ask for commit, push, or pull request creation.

Do not push incomplete experimental work unless the user explicitly asks to preserve or share it remotely.

### When to open a pull request

- Every branch whose changes are intended to enter `main` must go through a pull request.
- Opening the pull request is mandatory at the end of every completed repository-changing task; it is the handoff point for the designated independent code review, not permission to merge.
- Base the pull request on `main`.
- Do not open a pull request for investigation/read-only work with no repository change, abandoned experiments, or intentionally incomplete work unless the user explicitly asks for a draft pull request.
- Manual UI/E2E/integration QA may still be pending when the pull request is opened. Mark it clearly as pending.
- Use `.github/pull_request_template.md` and keep the pull request focused on one task.
- After independent review feedback, keep using the same branch and the same pull request. Apply requested corrections, validate the affected scope, commit the correction, push the same branch, and return the new commit SHA and existing pull request number for re-review.
- Never create a second pull request only to address review findings.

### Required handoff after opening or updating a pull request

Report all of the following to the user:

- Pull request number and URL.
- Branch name.
- Latest commit SHA.
- Implementing agent and designated independent reviewer.
- Concise scope summary.
- Automated validation executed, including canonical validation count, unit-test execution count, non-unit execution count, and retries.
- Manual QA still required, with exact steps or commands when applicable.
- Known risks, limitations, or intentionally deferred findings.

Then stop. Do not merge the pull request.

### Review and merge gate

- A pull request is not ready for `main` merely because implementation and automated validation succeeded.
- The mandatory gate is: new task branch created before editing -> implementation complete -> implementer self-review -> commit -> push -> pull request opened -> designated independent code review -> requested corrections resolved on the same branch/PR -> user manual QA when applicable -> user final review -> user performs the merge manually.
- Treat review findings as:
  - `BLOCKER`: must be fixed before merge.
  - `NON-BLOCKING`: valid improvement that does not block the current delivery.
  - `CLEAN`: no relevant issue found in the reviewed scope.
- If review returns a blocker, fix it on the same branch, commit, push, and report the new commit SHA and the existing pull request number for re-review.
- Agents must not approve their own work as a substitute for the designated review gate.
- Agents must never merge, squash-merge, rebase-merge, close, or otherwise finalize a pull request. This remains true even if the user says the change is approved or ready: approval means the agent must stop and hand control back to the user, who performs the merge manually.

## Reuse-first engineering

Vivat Flow is the repository source of truth and the reference implementation for this product. Build clean, reusable technical patterns with minimal business coupling, but do not prematurely create a generic framework or separate shared library.

- Before creating code, inspect the repository and identify the closest reference implementation.
- Reuse or extend established components, helpers, services, UI patterns, validation, API and authorization conventions, tests, infrastructure, scripts, and documentation whenever appropriate; do not introduce a parallel implementation for behavior already solved elsewhere.
- Decision order: reuse an existing implementation directly; evolve it with a small extension; extract a reusable abstraction only when duplication already exists or the pattern will clearly be reused; otherwise create a new implementation consistent with repository standards.
- Do not copy-paste repeated behavior across feature files. Prefer shared technical primitives with clear ownership, while keeping feature-specific domain configuration within its module.
- When a shared implementation is insufficient, understand its consumers, improve the shared foundation safely, preserve compatibility when appropriate, migrate affected consumers, and validate regressions.
- For every substantial development task, before coding, identify the reference implementation, what will be reused unchanged, what will be extended, and what genuinely must be created. State explicitly when repository inspection finds no suitable pattern.
- During review and in the final report, explicitly state what existing code was reused, what was extended, and what genuinely had to be created. Check for avoidable duplication in components, helpers, validation, styles, API/error handling, authorization, and test setup.
- For frontend work, also follow `docs/FRONTEND_STANDARDS.md`.

## Execution and validation discipline

- During normal AI-assisted development, automatically execute only targeted unit tests relevant to the changed scope. Static checks, compilation/build, and linting are allowed when they are proportionate to the change.
- **Never execute `./scripts/validate.ps1` automatically.** Canonical validation is opt-in and may run only when the user explicitly requests it.
- The AI may create, modify, refactor, and review E2E, integration, smoke, regression, performance, and other non-unit tests, but must not execute them automatically. Creating or changing such a test does not grant execution permission.
- Non-unit suites are run separately by a developer/team or dedicated CI/CD pipeline. When the AI changes a non-unit test, report its coverage, the manual command to run it, and explicitly that it was not executed.
- Classify backend tests with xUnit `Trait("Category", "Unit")` or `Trait("Category", "Integration")`. Tests without a `Unit` category are excluded from the AI's normal automated test scope.
- Before editing, inspect only the files needed to resolve the task, reuse established repository patterns, and identify the exact files expected to change.
- Do not perform open-ended repository hardening, opportunistic refactoring, or unrelated improvements. Report out-of-scope findings instead.
- During implementation, use the smallest permitted validation that proves the current change. Do not escalate from targeted checks to repository-wide validation without an explicit reason and user authorization when required.
- Prefer a single blocking command with an appropriate timeout over polling a running process. When a command already blocks until completion, wait for its result; do not poll logs or process state with `Start-Sleep`, `Get-Content`, `Get-Process`, or repeated status commands. Inspect logs only after completion or when investigating a genuine timeout or failure.
- When validation fails, diagnose the specific failure before rerunning it. Do not rerun an unchanged failing command unless the failure is known to be transient, and do not escalate a targeted failure to repository-wide testing without cause.
- Stop once the requested implementation and required acceptance gates pass. Report optional findings or remaining risks without beginning another review, hardening, cleanup, optimization, or validation cycle.

### Cost-aware validation / fast feedback

The default AI validation proceeds through permitted checks only:

cheap/static checks -> targeted unit tests -> proportional build/lint/static validation.

`./scripts/validate.ps1` is excluded from the default flow and runs only when the user explicitly requests canonical validation.

#### 1. Static/preflight first

Before running builds or unit tests:

- Inspect only the changed/relevant files.
- Run targeted `rg`/static checks when useful.
- For changed non-unit tests, review locator uniqueness, labels, async/loading states, and obvious assertions statically before handing them off.

Do not launch an expensive test runner to discover basic spelling, locator, or selector problems that can be found statically.

#### 2. Canonical validation is opt-in only

The canonical repository validation is `./scripts/validate.ps1`, but it is **not** part of the default AI development loop.

- Do not run it because a task is complete.
- Do not run it because a pull request is about to be opened.
- Do not run it after review fixes unless the user explicitly requests it.
- If the user explicitly requests canonical validation, run it at most once after the implementation is stable unless a concrete failure requires a justified rerun.

#### 3. Proportional validation after changes

- If production frontend code changes, run the minimum relevant frontend lint/build/static check.
- If backend production code or contracts change, run the relevant targeted backend unit tests and the minimum build/static check needed for confidence.
- If only documentation changes, runtime validation is not required.
- If only non-unit test files, selectors, assertions, synchronization, or test-only helpers change, do not execute the non-unit suite automatically; report the affected manual/CI command instead.

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
- Use targeted `dotnet` build/test commands for the backend when they are the smallest permitted validation for the task. Prefer `--no-restore` when restored assets are current; use `--no-build` only when the required output is known to be current.
- Run `.\scripts\validate.ps1` only when the user explicitly requests canonical validation. It remains the canonical combined Release build + unit-test + frontend lint/build runner, but it is not an automatic completion gate.
- If backend assets are missing or dependencies changed, explicitly run `.\scripts\restore.ps1` once, then rerun only the targeted validation needed for the task (or the canonical runner if the user explicitly requested it). Do not repeatedly retry a failing package source.

## Local application runtime

### Permanent DEMO / DEV boundary

The customer-facing Cloudflare environment is DEMO. Normal local work is DEV only: use `scripts\start-local.ps1`, which owns frontend `5175`, API `7227`, and the separate `vivatflow_dev` logical database on the shared PostgreSQL port `5432`. DEV and DEMO may share the PostgreSQL instance, but never the same database. Promotion to DEMO is an explicit operation after QA through `scripts\start-demo.ps1`; DEV is never exposed through Cloudflare. The canonical operational contract is `docs/ENVIRONMENTS.md`: DEMO is persistent (`5173`/`7226`/`tsdt`) and PREVIEW is disposable only when explicitly requested.

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
- Never issue `dotnet run` or `npm run dev` directly against ports 7226 or 5173 without first using `scripts\start-demo.ps1`. A restart must stop the project-owned process and wait for its port to be released before starting a replacement.

After relevant changes, the final response should clearly state that the application is available and provide the URLs needed for manual validation.

Never stop the application automatically after validation. Stop it only when explicitly requested by the user.

### Docker-backed local workflow

- When Docker is running and the local application environment has been started, keep the localhost environment running unless the user explicitly asks to stop it.
- After meaningful application code changes, rebuild or update every affected local Docker image before runtime validation. When application services are defined in Docker Compose, use the updated images for the local validation environment.
- The current Compose configuration provides PostgreSQL only. Do not introduce application containers solely to satisfy this workflow; apply the image rebuild requirement to application images once they exist.
