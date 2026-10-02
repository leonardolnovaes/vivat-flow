# Review and pull request handoff

Read when reviewing a repository change or preparing a commit, push, or pull request. The universal bootstrap in `AGENTS.md` also applies. Review Rule IDs and reviewer topology are bound in `.ai-engineering/adoption.yml`; resolve the invoked rules from its exact framework checkpoint.

## Review policy

- Existing Vivat Flow engineering, validation, Git, security, QA, and runtime policy stays authoritative until evaluated rule by rule. Do not assume an existing rule is cheap, costly, or obsolete merely because it exists.
- The implementer self-reviews the complete final diff for every meaningful change. This is not independent review.
- Independent AI review follows the reviewer topology in the adoption manifest. If the designated reviewer is unavailable, mark review pending; do not substitute the implementer's self-review.
- The independent reviewer performs normal technical review and the AI Engineering efficiency review using the pinned review protocol, including cost-aware behavior. For framework-dependent handoffs and reviews, report the framework repository, resolved SHA, and Rule IDs actually consulted. Local operational policy controls choices the framework leaves to the project.
- Cross-agent review is `EXPERIMENTAL`. Measure review escapes, retries, validation cost, and useful findings; revisit the topology when evidence or tooling changes.

## Commit and pull request

- `main` is the stable integration branch. Never implement meaningful changes directly on it or push feature work to it. Review corrections stay on the same task branch and pull request.
- Commit a cohesive, complete, reviewable scope only after permitted validation passes or is documented as inapplicable. Do not create a knowingly broken or incomplete checkpoint unless explicitly asked. Stage only task files; use a concise Conventional Commit message. Do not commit every small edit.
- At completion, inspect the working tree, validate within policy, commit, push the branch to `origin`, and open a focused PR targeting `main` using `.github/pull_request_template.md`. Do this automatically for every completed repository-changing task. Do not push incomplete experiments or open a PR for investigation, abandoned, or incomplete work unless explicitly asked to preserve/share it or open a draft.
- Mark pending manual UI, E2E, or integration QA in the PR. After review feedback, fix blockers on the same branch/PR, validate the affected scope, commit, push, and return the new SHA and existing PR number. Never open a second PR only for review fixes.
- Report PR number and URL, branch, latest commit SHA, implementer, designated reviewer, concise scope, validation counts and retries, exact pending manual QA steps/commands, and known risks or deferred findings. Then stop.

## Review and merge gate

- The gate is: branch before edit → complete implementation → implementer self-review → commit → push → PR → designated independent AI review → correction of blockers on that PR → applicable user manual QA → user final review and manual merge. Successful automation alone does not make a PR ready for `main`.
- `BLOCKER` must be fixed before merge; `NON-BLOCKING` is valid but does not block this delivery; `CLEAN` means no relevant issue in reviewed scope. Report a corrected commit for re-review after fixing a blocker.
- Agents never approve their own work as independent review and never merge, squash-merge, rebase-merge, close, or otherwise finalize a PR, even when the user says it is approved or ready. The user alone performs the final merge.
