# Frontend Standards

This is the canonical quality standard for new TSDT ERP screens and meaningful frontend changes.

## Page hierarchy and space

- Give every business screen an intentional hierarchy: header and primary action, contextual KPIs when useful, filters before results, results, then pagination or empty state.
- Do not append filters after the content they affect.
- Use desktop width intentionally. Avoid giant empty regions, unnecessary nested cards, narrow content islands, and making every read-only value a card.
- A workflow screen must communicate its business state and available next action, not only database fields.

## Lists, status, and detail screens

- Define a sensible server-side default scope and deterministic backend ordering for each business list.
- Provide relevant search and filters, visible active-filter state, `Limpar filtros`, loading, error, empty, filtered-empty, and pagination states where applicable.
- Centralize domain-status mappings. UI status text is pt-BR, uses an accessible semantic badge when appropriate, never exposes enum identifiers, and never relies only on color.
- Detail pages have a clear header, status, key context, grouped business information, valid workflow actions, and persisted history when the domain supports it. Do not default to one giant passive card.

## Forms and dialogs

- Group fields logically; use visible labels, pt-BR validation, required indicators, server-validation handling, saving states, duplicate-submit prevention, and recoverable errors.
- Use application-controlled dialogs rather than browser `alert()` or `confirm()` for product workflows. Dialogs need an explicit purpose, clear actions, safe cancel, validation, loading state, and preserved input after recoverable errors.

## Responsiveness, accessibility, and authorization

- Verify wide desktop, notebook, and narrow/mobile layouts for every meaningful UI change. Do not squeeze an unusable desktop table into mobile; use stacking or responsive cards when needed.
- Controls must be keyboard usable, have visible labels and focus, adequate target size and contrast, and status meaning beyond color.
- Frontend visibility is only UX. Backend authorization remains authoritative.

## Reuse, language, and quality gate

- Inspect existing buttons, dialogs, badges, loading states, form controls, and layouts before introducing local UI. Extract a component only for a genuinely repeated pattern.
- Technical identifiers, source, contracts, and developer documentation are English. All user-facing UI is pt-BR.
- Build, lint, and automated tests alone do not complete meaningful UI work. Inspect the running screen, verify correct data and authorization, error/conflict states, visual hierarchy, responsive behavior, and browser console/page/network errors.
