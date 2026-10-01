# Agenda MVP manual QA

Status: pending running-screen visual/manual QA and ChatGPT code review. Unit tests and builds do not satisfy this gate.

## DEV runtime

Run `./scripts/start-local.ps1` (use `-Restart -BackendOnly` after API changes). Keep DEV running.

- Frontend: http://127.0.0.1:5175/agenda
- API: https://localhost:7227
- Health: https://localhost:7227/health

Use existing DEV tenant ADMIN, MANAGER, USER, and Platform Administrator accounts. Never use DEMO for these scenarios.

## Fixture preparation through existing flows

Create OS records from active Contracts in DEV. Assign records to two different eligible professionals. Use different Contracts for concurrent unfinished records. Set dates in OS planning, save, then explicitly schedule.

Include Scheduled, InProgress, AwaitingClosure, Closed, Cancelled, and a Draft with tentative dates. Include an OS with at least four historical services, more than three OS on one day, a multi-day OS crossing midnight, a second tenant's scheduled OS, and an empty day. Note the OS ids and date ranges for comparison with Agenda.

## ADMIN and MANAGER

1. Verify Agenda appears beside Ordens de Serviço; open `/agenda` as each role.
2. Switch Dia, Semana, Mês. Check Hoje and previous/next across month and year boundaries. Week starts Monday; month includes complete weeks and adjacent dates.
3. Filter each professional, compare with assigned OS, then Limpar filtros. An unknown or foreign tenant professional must return no entries.
4. Confirm Scheduled, InProgress, AwaitingClosure, and Closed appear with valid dates; Draft with dates and Cancelled never appear.
5. Verify multi-day OS appears on every overlapping day and an OS ending exactly at midnight does not appear on the following day. Compare local displayed dates/times with OS planning.
6. Verify historical customer, address, responsible name, services, and readable status text. Cards show two services plus remaining count. Month shows three OS plus overflow; click overflow/day header to open Dia. Click Ver OS and a month event and confirm the correct OS detail opens.
7. Visit empty dates and empty professional scopes; verify clear messages. Use browser offline mode then Atualizar to verify recoverable errors; restore network then Tentar novamente. Throttle requests and navigate rapidly to check loading and stale-response protection. Block the eligible-assignees request to verify its independent error/retry.

## USER and platform isolation

1. Log in as USER. Verify Agenda navigation and personal description; no professional filter. Compare every result with assigned OS.
2. In browser DevTools, run the following read with the authenticated USER session, replacing dates and `OTHER_USER_ID`. Results must still contain only the authenticated user's assigned OS:

   ```js
   const query = new URLSearchParams({ from: '2026-10-01T00:00:00-03:00', to: '2026-10-08T00:00:00-03:00', assignedUserId: 'OTHER_USER_ID' });
   const response = await fetch(`/api/work-orders/agenda?${query}`, { credentials: 'include' });
   console.log(response.status, await response.json());
   ```

3. Repeat with another tenant's professional id. No foreign tenant records may appear. Directly opening another professional's OS must remain denied/not found.
4. As Platform Administrator, verify no Agenda tenant navigation, `/agenda` routes to access denied, and the same API read returns 403. As unauthenticated user it must return 401.
5. As management, manually call the API with missing boundaries, absent timezone, invalid dates, equal/reversed boundaries, and a range exceeding 62 days. Expect 400 with Portuguese field messages. Verify a valid `Z` interval and equivalent `-03:00` interval return the same ids.

## Visual and accessibility gate

Inspect day/week/month at 1920px desktop, 1366px notebook, and 390px mobile. Week days stack on notebook/mobile; month becomes a chronological list on mobile. Check long customers, service names, addresses, multiple events, visible focus, keyboard controls, readable status text, Today labels, no horizontal page overflow, and adequate touch targets. Check browser console and failed network requests, excluding deliberate failure scenarios.

Record actual results before final review/merge. No automated E2E, Integration, smoke, or performance suite was executed for this delivery.
