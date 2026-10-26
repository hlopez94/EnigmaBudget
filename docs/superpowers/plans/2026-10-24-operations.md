# Daily finances, card cycle and monthly shared collections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. User explicitly authorized immediate execution using Luna agents; proceed without another approval round.

**Goal:** Make EnigmaBudget usable after onboarding: manage accounts/cards, record income/expenses/transfers, buy in installments, reconcile/pay statements and collect shared installments using each month's own allocation.

**Architecture:** Extend the existing single ASP.NET10 host and PostgreSQL ledger with focused finance endpoints/services. Angular22/OptimusUI consumes the contract below with persistent navigation and real forms. No mock financial data or inactive feature links.

**Tech Stack:** .NET10, EF/Npgsql, PostgreSQL17, Angular22/OptimusUI2, xUnit/Vitest/Playwright. All agents and reviewers gpt-6-luna per user preference.

**Spec:** docs/producto/funcionalidades-y-circuitos.md; monthly sharing correction confirmed in section16 on24Oct2026. Existing design's stack/privacy/security constraints remain binding.

## Global Constraints

- Keep current branch codex/rebuild-net10-angular22 and PR4; preserve main and all existing data.
- Money is invariant decimal strings at API boundaries, numeric(18,2) in PostgreSQL. ARS/USD independent; exact cent allocation, no float arithmetic.
- Authenticated subject determines workspace; CSRF on mutations, no-store on API, secure Production cookies. Reuse existing auth; no additional identity provider.
- Each real bank effect is one ledger entry; pending operations and card purchases do not debit a bank account. Paying a card is not another expense. Collecting from a participant never reduces bank-card debt.
- Idempotent POST operations use client-generated UUID id; same id+payload replays return existing result, changed payload409. Ownership, resource validation and rollback are server-side.
- Mutable aggregates use optimistic versions; atomic transactions and constraints protect duplicate payments, overcollections and stale rule changes. Do not catch arbitrary database failures as success.
- Dates are date-only, periods YYYY-MM. Preserve historical/onboarding rows with an additive migration; no database resets. No private sources, credentials or reports committed.
- User-facing Spanish; forms accept comma decimals normalized to invariant strings. Native-looking touch-friendly controls using OptimusUI; no Ionic.
- Follow the shared contract docs/api/operaciones.md. Inherited tests continue to pass.

## Review Focus

- Cross-workspace account/card/group IDs and missing CSRF: reject with404/401/400 and no writes (Task1).
- Duplicate/retried income, purchase, payment, collection or transfer: no duplicate bank/debt effects (Task1).
- Monthly ratio changes: future changes propagate, confirmed/collected months immutable; missing rule only provisional (Task1/2).
- Money rounding, ARS/USD separation, partial payments and old balance carryover: exact and counted once (Task1/3).
- Completed-empty onboarding is not a dead end; reload, server errors and double-clicks preserve local form/data (Task2/3).

## Contract

The API/DTO contract and behavioral cases are in docs/api/operaciones.md. That file is supplied separately to every worker. It owns field names and routes; changes must be reported to controller before downstream work.

## Task 1: Operational finance backend

**Files:** Create src/EnigmaBudget.Api/Finance/{Contracts.cs,FinanceEntities.cs,FinanceEndpoints.cs,MoneyRules.cs,AccountService.cs,CashService.cs,CardService.cs,SharingService.cs,FinanceQueries.cs}; create Identity/WorkspaceService.cs if needed to share existing provisioning logic. Modify Data/AppDbContext.cs and Program.cs minimally, generate additive migration/snapshot. Tests: tests/EnigmaBudget.Api.Tests/{FinanceIntegrationTests.cs,SharingIntegrationTests.cs,CardCycleIntegrationTests.cs} and focused utilities as needed. No frontend edits.

**Interfaces:** Consumes existing auth/session/CSRF and workspace provisioning; produces all finance API routes and exact DTOs in shared contract. Replace original /accounts and /cards GET mappings with extended compatible endpoints, not duplicate routes. Keep original DTO fields for onboarding compatibility. Existing ApiFactory provides isolated PostgreSQL schema and test-only identity.

- [ ] Step1: Write behavior integration tests covering accounts after completed-empty onboarding; paid/pending/partial income+expense; same-currency transfer; idempotency and ownership; card purchase split, statement reconciliation and partial/full payment; monthly60/40 then30/70 allocations, explicit future update, month lock and collections; exact-cent splits and missing-rule provisional state. Use cases in contract, tests before implementation. Capture meaningful RED.
- [ ] Step2: Implement exact-money helpers, entities and constraints, additive migration, focused services/endpoints. Ledger entries are the only bank deltas; query balance from opening+entries. Use immutable cash effects with void/reversal where exposed. Account/card maintenance remains accessible after onboarding. Group participants are manual named participants including exactly one self; authenticated invitations are not falsely offered.
- [ ] Step3: Implement monthly allocation using the rule of each installment period. Rules support Equal/Percentage/Income; largest-remainder cents partition, deterministic ties. Recompute unconfirmed future receivables on rule change, not locked/collected months. Collection locks month atomically. State shows unknown/provisional periods and forbids collecting until actual rule exists.
- [ ] Step4: Implement card obligations, statement reconciliation, charges/credits and partial payments with explicit bank/obligation amounts for FX. Use cumulative obligations minus applied payments to avoid duplicate carryover. Do not sum statement totals into card debt. Guard concurrent mutations with versions. Run focused tests while iterating, full dotnet test and build once before commit.
- [ ] Step5: Commit only backend/migration/tests; full ignored report with RED/GREEN, endpoints, counts, commands and deviations. Return DONE/status, SHA and report path. Controller reviews before Task2.

## Task 2: Usable Angular financial workspace

**Files:** Extend web/src/app/core/api.models.ts/api.service.ts, app.ts/app.html/app.css; replace dashboard dead-end with actionable overview. Create web/src/app/finance/{finance-shell.ts,finance-shell.html,finance-shell.css,accounts.*,operations.*,cards.*,sharing.*,finance.spec.ts} with focused components as appropriate. Preserve existing onboarding/sign-in tests.

**Interfaces:** Consumes Task1 contract unchanged. Navigation: Inicio, Movimientos, Cuentas, Tarjetas y resúmenes, Compartidos. Authenticated users can navigate regardless of onboarding completion, with optional setup action. Mutations await API then refresh affected overview; local errors keep edits. No simulated client-only persistence.

- [ ] Step1: Failing tests for action accessibility after empty onboarding, monetary normalization, deferred saves/errors and correct refresh/selection state; write meaningful component/service behavior regressions rather than string-mirroring templates.
- [ ] Step2: Implement persistent navigation and actions. Accounts: add/edit labels/archive with balances, optional balance adjustment. Movements: income/expense paid or pending, partial payment/collection, transfer, history filters and reversal. Cards: add/edit, purchase with installments and optional group, full schedule, statement period/currency review with editable due date/real total/charges, confirm and register partial/full payments. Sharing: create group/participants, set monthly mode/weights, show self/others shares, lock month and collect pending amounts into a selected account.
- [ ] Step3: Surface each month's own allocation and provisional fallback explicitly. Support October60/40 and November30/70 without overwriting October. Show current bank balances, total card debt, selected-month installments/statement pending and participant receivables separately. Protect form edits during requests and recover conflicts without reload loss; signals/markForCheck for zoneless Angular. All empty states have working actions.
- [ ] Step4: Run frontend suite/build; inspect real browser at360px and desktop (Task3 adds persisted regression). Maintain readable contrast and keyboard labels. No unimplemented savings/import/Android/paid-plan buttons advertised as working.
- [ ] Step5: Commit only UI; ignored report with exact tests/build, meaningful RED, limits and API usage. Controller reviews before Task3.

## Task 3: Complete real-browser acceptance and publishable preview

**Files:** Extend web/e2e with finance-workflow.spec.ts and monthly-sharing.spec.ts or cohesive acceptance; adapt scripts/verify.sh/playwright config only if necessary; update README and status docs. Any integration fixes must have a reproduced regression. Preserve users' preview data.

**Interfaces:** Uses backend/frontend from Tasks1/2 and existing rootless Podman/Compose on5081. Live DB preserved; tests use isolated new database via explicit env. No test data is written into user's preview database.

- [ ] Step1: Real browser test from signed-in empty/completed onboarding: add ARS accounts/card; income, expense, transfer; purchase120000/3; group60/40 month1 and30/70 month2; confirm month1, collect8000 twice idempotently, prevent month1 ratio change; confirm statement40000, pay20000 twice with unique IDs and verify debt80000 after two payments and pending0; reload and inspect all real balances. Use server response waits, no sleeps/mocked API.
- [ ] Step2: Verify split still changes future month only, partial collections/payment amounts and CTA visibility; add responsive360px check. Reject cross-owner and missing-CSRF mutations remain backend tests. Inspect screenshots and actual contrast.
- [ ] Step3: Run scripts/verify.sh with fresh test DB, build/publish and image build. Recreate only preview app5081 after additive migration; do not stop/reset PostgreSQL/Garage volumes or drop user data. Check current user can see/use new navigation. Update README to distinguish delivered capabilities and future product modules.
- [ ] Step4: Commit tests/docs/integration fixes; ignored report with all commands/results, preview URL and limitations. Controller performs final cross-task review, publishes to same branch/PR, checks CI and preserves preview. No merge to main.

## Completion requirement

Do not stop at scaffolding or inactive forms. This plan is complete only when the entire specified manual cycle is usable in the live preview and real PostgreSQL/browser tests. Future imports/automatic statement extraction, DolarAPI fetch, goals/projection expansion, actual invitations, Android and subscriptions remain separately tracked, without pretending these are implemented.
