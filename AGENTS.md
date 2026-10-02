# Vivat Flow repository bootstrap

Vivat Flow is the source of truth for this product. Stay within MVP scope, preserve the modular monolith, and avoid unrelated changes or unnecessary dependencies. Read relevant `docs/` before architectural changes and explain important architecture changes before implementing them.

Enforce authorization in the backend, use secure defaults, and never expose, log, or commit secrets. Do not add sensitive personal or medical data without an explicit requirement.

Write code, technical artifacts, internal logs, and developer documentation in English. Keep user-facing application content in Portuguese (Brazil), structured for future localization.

Before editing repository files, inspect the branch and working tree. For a new task, update `main` and create a dedicated short-lived task branch from current `main`. Preserve unrelated changes. Agents never merge, close, or otherwise finalize a pull request; the user makes the final merge decision and performs the merge manually.

Vivat Flow adopts AI Engineering through `.ai-engineering/adoption.yml` and routes local context through `.ai-engineering/context-routing.yml`. Read both manifests, select the smallest relevant route or routes, and read their local resources **before implementation**. Explicit Rule IDs resolve only through the framework source and exact immutable SHA in the adoption manifest; report unavailable rules instead of substituting `main` or reconstructing them from memory. A route without an invoked Rule ID does not require external framework rule documents.

If no route clearly covers a task, inspect only enough local context to classify it, then expand deliberately when uncertainty or risk requires it. Do not preload all routes. Universal security, branch, and merge constraints above apply regardless of route.
