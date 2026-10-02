# Engineering and validation policy

Read for implementation, test, tooling, and repository configuration work. The universal bootstrap in `AGENTS.md` also applies.

## Reuse and scope

- Vivat Flow is the source of truth and reference implementation. Build reusable technical patterns with minimal business coupling; do not create a generic framework or separate shared library prematurely.
- Before coding, inspect only relevant files and identify the closest implementation. Reuse it directly, extend it modestly, or extract a shared abstraction only for existing duplication or clear reuse. Otherwise create a new implementation consistent with repository conventions.
- Reuse established components, helpers, services, UI patterns, validation, API and authorization conventions, tests, infrastructure, scripts, and documentation. Do not copy repeated behavior across feature files. Keep shared technical primitives with clear ownership and feature-specific configuration in its module.
- If a shared foundation is insufficient, inspect its consumers, extend it safely, preserve compatibility where appropriate, migrate affected consumers, and validate regressions.
- For substantial work, state the reference implementation, what was reused or extended, and what had to be created. State when no suitable pattern exists. In self-review and handoff, check for avoidable duplication in components, helpers, validation, styles, API/error handling, authorization, and test setup.
- Keep changes within requested scope. Report unrelated findings without open-ended hardening, refactoring, or new dependencies. Frontend work also follows `docs/FRONTEND_STANDARDS.md`.

## Execution and validation

- Before builds or tests, inspect the changed files and run useful targeted static checks. For changed non-unit tests, inspect locator uniqueness, labels, loading and async states, and assertions statically. Do not use an expensive runner to discover basic selector or spelling errors.
- Normal AI execution is limited to targeted unit tests relevant to the change, plus proportionate static, build, or lint checks. Use the smallest permitted check that proves the change; do not escalate to repository-wide validation without a concrete reason and required authorization.
- `./scripts/validate.ps1` is the canonical combined Release build, unit-test, and frontend lint/build runner. Run it only when the user explicitly requests canonical validation: never merely for completion, PR opening, or review fixes. When requested, run it at most once after stability unless a concrete failure justifies a rerun.
- The AI may create or review E2E, integration, smoke, regression, performance, and other non-unit tests but never runs them automatically. Creation or modification does not grant execution permission. Report their coverage, exact manual/CI command, and that they were not run.
- Classify backend xUnit tests with `Trait("Category", "Unit")` or `Trait("Category", "Integration")`; tests without `Unit` are outside normal AI automated execution.
- For production frontend changes, run the minimum relevant lint, build, or static check. For backend production code or contract changes, run targeted backend unit tests and the minimum build/static check. Documentation-only changes need no runtime validation. Non-unit-test-only changes do not trigger non-unit execution.
- Prefer one blocking command with a suitable timeout. Do not poll a blocking process with `Start-Sleep`, `Get-Content`, `Get-Process`, or repeated status checks; inspect logs after completion or a genuine timeout/failure.
- On failure, inspect the complete output, distinguish application, test, and environment defects, batch related corrections, then rerun only the affected scope. Do not repeat an unchanged failing command unless the failure is known to be transient.
- Stop after the requested implementation and required gates pass. Report optional risks without another speculative review or validation cycle.
- For substantial changes, report canonical, unit, and non-unit execution counts, plus retries and causes. Record explicit authorization for any non-unit execution.

## Development environment

- Use VS Code, not Visual Studio or its Just-In-Time Debugger. Run .NET applications, tests, migrations, and tooling through `dotnet` CLI. If `dotnet` crashes, capture and investigate the terminal exception or stack trace with VS Code-compatible debugging.
- Use targeted `dotnet` build/test commands. Prefer `--no-restore` when assets are current; use `--no-build` only when required output is current.
- If backend assets are missing or dependencies change, run `.\scripts\restore.ps1` once, then only targeted validation (or canonical validation if explicitly requested). Do not repeatedly retry a failing package source.
