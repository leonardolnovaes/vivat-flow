# Vivat Flow repository bootstrap

Vivat Flow is the source of truth for this product. Stay within MVP scope, preserve the modular monolith, and avoid unrelated changes or unnecessary dependencies. Read relevant `docs/` before architectural changes and explain important architecture changes before implementing them.

Enforce authorization in the backend, use secure defaults, and never expose, log, or commit secrets. Do not add sensitive personal or medical data without an explicit requirement.

Write code, technical artifacts, internal logs, and developer documentation in English. Keep user-facing application content in Portuguese (Brazil), structured for future localization.

Before editing repository files, inspect the branch and working tree and preserve unrelated changes; stop and report a conflict if they cannot be safely isolated. For a new task, update `main` and create a dedicated short-lived branch from it: `feat/`, `fix/`, `refactor/`, `chore/`, `docs/`, or `test/`. Review fixes stay on the existing PR branch. Do not create extra workflow branches. Complete repository changes with a commit, push, and PR; read the review and handoff route before those steps. Agents never merge, close, or otherwise finalize a PR; the user performs the final merge.

Read `.ai-engineering/context-routing.yml`, select the smallest relevant route or routes, and read their local resources before each relevant phase of work. `.ai-engineering/adoption.yml` owns AI Engineering bindings: read it when a review activity or another explicit Rule ID invocation requires framework resolution. Resolve only invoked Rule IDs at its exact immutable SHA; report unavailable rules instead of substituting `main` or reconstructing them from memory. Routes without Rule IDs do not trigger external framework loading.

AI validation runs only relevant targeted unit tests and proportionate static, build, or lint checks. Never run non-unit suites automatically or `scripts/validate.ps1` without the user's explicit request. The routed engineering policy gives the detailed execution rules.

If no route clearly covers a task, inspect only enough local context to classify it, then expand deliberately when uncertainty or risk requires it. Do not preload all routes. Universal security, branch, and merge constraints above apply regardless of route.
