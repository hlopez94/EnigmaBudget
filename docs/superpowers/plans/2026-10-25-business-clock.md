# Argentina financial dates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development task by task. User authorized complete app implementation with Luna; proceed continuously.

**Goal:** Make current balances and confirmed financial-date guards use the Argentine business date consistently.

**Architecture:** Inject one business clock backed by TimeProvider into the existing .NET host. Reuse it for current date/period; preserve DateOnly fields, historical rows, current API DTOs and all monetary invariants.

**Tech Stack:** .NET10, EF/PostgreSQL, xUnit; no new framework or external provider.

**Spec:** docs/superpowers/specs/2026-10-25-business-clock-design.md; docs/api/operaciones.md.

## Global Constraints

- Same feature branch and PR4; no main merge, no user DB reset or data changes.
- All agents Luna. Heavy tasks sequential under work/with-ram-limit.sh4Gi/512Mi; refuse below6Gi available, dotnetmaxcpu1/shared compilation off.
- Existing API amount strings, ARS/USD isolation, ownership, CSRF and idempotency unchanged. No global static mutable test clock.
- Argentina zone America/Argentina/Buenos_Aires for business days; UTC for technical instants. Preserve future planned obligations and committed installment debt.
- Preview5082/PG55433 is user data; tests only isolated schemas on PG55432. No unrelated service changes.

## Review Focus

- UTC month rollover must not admit tomorrow's real cash payments or choose the wrong rule month.
- Both account/card endpoints and finance overview use matching current-date cutoffs.
- Old future-dated ledger entries are retained but not prematurely included in current balances.
- Future installments remain total card commitments even when current statement payable is smaller.
- Injected clocks are scoped to each host/test, so parallel factories cannot affect each other.

## Task 1: Business clock and consistent date guards

**Files:** Create src/EnigmaBudget.Api/Finance/BusinessClock.cs. Modify Program.cs DI; Finance/{AccountService,CashService,CardService,SharingService,FinanceQueries}.cs only where business-today/default-period/current-balance applies. Modify reusable ApiFactory constructor in tests/EnigmaBudget.Api.Tests/OnboardingIntegrationTests.cs to optionally replace TimeProvider without breaking existing callsites. Create tests/EnigmaBudget.Api.Tests/BusinessClockIntegrationTests.cs. No frontend, migration or DTO changes.

**Interfaces:** BusinessClock(TimeProvider) exposes UtcNow:DateTimeOffset, Today:DateOnly and CurrentPeriod:string for Argentina; register singleton TimeProvider.System + clock. Financial handlers obtain that service through DI or a focused shared resolver. ApiFactory optional TimeProvider overrides host DI only. Existing amount format/query interfaces remain stable.

- [ ] Write real PostgreSQL regressions with isolated fixed clocks at2026-11-01T01:30Z and03:30Z. Before Argentina midnight: Nov1 paid income/expense/payment/transfer/adjustment/cardpayment/collection rejects without writes; pending operation remains allowed. Default group month October. After midnight: Nov1 applicable openings/payment succeeds. Use focused representative fixtures, no large stress loops.
- [ ] Assert future account opening excluded in GETaccounts and finance overview; seed a legacy future ledger row only in isolated test schema and assert excluded before effective date/included afterward. Cards future opening excluded consistently; future installments still included as commitments. Capture meaningful RED.
- [ ] Implement injected clock and replace all financial UTC-date/default-month usages. Keep technical timestamps UTC and incoming date strings unchanged. Current account balances apply opening date and ledger cutoff; account adjustment at supplied date keeps its explicit historical cutoff.
- [ ] Run focused tests then full backend Release suite under resource cap. No frontend rebuild on unchanged source. Commit backend/tests and ignored report task-1-report.md exact RED/GREEN/commands/SHA/remaining risks.

## Task 2: Verify boundary behavior and update local host safely

**Files:** README/product tracker status if appropriate; any needed regression fix with reproduced evidence. No new modules hidden inside this task.

**Interfaces:** Root-owned live5082 host PID in work/preview5082.pid, independent published DLL plus original contentRootsrc/EnigmaBudget.Api; static frontend preserved. Publish a new directory, never overwrite live DLL or reset user DB. Stop only verified own server with SIGTERM, start detached via RAM wrapper with same contentroot/auth/config; no forced browser reload. Existing compose5081 runtime issue is independent.

- [ ] Review date guards/current-balance queries and passing regression evidence; preserve unchanged UI and latest mobile assets. Run appropriate inherited checks once; no duplicate UI suites for backend-only change absent concrete concern.
- [ ] Publish separate artifacts directory under cap, safely switch only the own5082 server and verify health/runtime version. Record constraints and update tracker to completed only for this date block.
- [ ] Push same branch/PR and inspect CI, preserving draft/no main merge. Continue next block: tenants/RBAC and remaining product scope. Do not complete global goal here.
