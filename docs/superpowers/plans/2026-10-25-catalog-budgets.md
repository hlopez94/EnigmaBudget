# Catálogo y presupuestos mensuales Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace free-text-only classifications with workspace catalogs and add explainable monthly budgets per category and currency.

**Architecture:** Add workspace-scoped category/merchant/income-source catalogs while keeping each existing category string as the historical display snapshot. Add versioned monthly budgets and an explainable query that reads current Expense operations, actual card installments and confirmed statement adjustments once. When Recurring/Healthcare is integrated later, its plan extends this query to consume the new shared `OperationSettlementCalculator`; it owns that future settlement logic, so this plan never duplicates it.

**Tech Stack:** .NET 10, EF Core, PostgreSQL, ASP.NET minimal APIs, Angular 22, OptimusUI 2, Vitest, Playwright.

**Spec:** `docs/superpowers/specs/2026-10-25-catalog-budgets-design.md`; also follow `docs/superpowers/specs/2026-10-24-finanzas-design.md` and `docs/producto/funcionalidades-y-circuitos.md`.

## Global Constraints

- Angular 22 + OptimusUI 2 (the release compatible with Angular 22), .NET 10, PostgreSQL.
- One ASP.NET host serves the API and compiled Angular frontend. No Ionic or microservices.
- No private spreadsheet links, financial data, credentials, or tokens in this public repository.
- Amounts API as decimal strings; PostgreSQL money storage `numeric(18,2)`.
- Enforce membership server-side; never trust a tenant ID or identity supplied by the client.
- Development sign-in is explicitly opt-in and forbidden outside Development. Google sign-in must fail closed when unconfigured.
- Spanish user-facing copy; ARS and USD totals remain separate.
- Mutations validate CSRF; request IDs are workspace-idempotent with identical-payload replay and changed-payload 409; editable resources use version checks.
- Catalog/budget writes use `manageResources`; reads use `read`; finance record permissions continue to use `recordFinance`.
- Check available RAM before builds/tests/browser suites/container builds and pause below 6 GiB; run heavy processes sequentially, each ≤4 GiB RAM/512 MiB swap; Node heap ≤2 GiB, Angular/Vitest workers ≤2, Playwright workers ≤1; container builds need a separate cap.

## Review Focus

- Blank/legacy labels, case/spacing-distinct historical buckets and identical names in different workspaces retain source history without inference — Task 1 `legacy_category_backfill_preserves_labels_and_is_workspace_scoped`.
- Foreign IDs and PersonalReadGrant cannot enumerate or mutate a catalog — Task 2 `foreign_catalog_id_is_not_found_and_shared_reader_is_read_only`.
- Category rename/archive preserves every historic snapshot — Task 3 `rename_does_not_rewrite_history_or_snapshot`.
- Card installments are budgeted once, never purchase total + statement/payment — Task 4 `monthly_budget_counts_cash_expense_and_card_installment_once`.
- ARS/USD filters and partial operation payments cannot change or combine gross-period consumption — Task 4 `partial_operation_payment_does_not_change_budget_spend`.

---

### Task 1: Workspace catalogs and safe historical backfill

**Files:**
- Create: `src/EnigmaBudget.Api/Finance/CatalogEntities.cs`
- Modify: `src/EnigmaBudget.Api/Data/AppDbContext.cs`
- Generate EF migration `FinanceCatalogs` plus its generated designer/model snapshot
- Test: `tests/EnigmaBudget.Api.Tests/FinanceCatalogMigrationTests.cs`
- Create: `src/EnigmaBudget.Api/Finance/CatalogDefaults.cs` for pure initial-category construction
- Modify: `src/EnigmaBudget.Api/Identity/WorkspaceProvisioner.cs`, `src/EnigmaBudget.Api/Workspaces/WorkspaceEndpoints.cs` to add defaults atomically when a new workspace is created

**Interfaces:**
- Add `FinanceCategory(Guid Id,Guid WorkspaceId,string Kind,string Name,bool IsArchived,int Version)`, `FinanceMerchant(Guid Id,Guid WorkspaceId,string Name,bool IsArchived,int Version)`, `FinanceIncomeSource(Guid Id,Guid WorkspaceId,string Kind,string Name,bool IsArchived,int Version)`.
- Category kind `Expense|Income`; income-source kind `Salary|Extra|AnnualBonus|IndependentWork|Other`. Names are trimmed on new writes and compared exactly/case-sensitively; unique indexes are workspace scoped: category `(WorkspaceId,Kind,Name)`, merchant `(WorkspaceId,Name)`, source `(WorkspaceId,Kind,Name)`.
- `FinanceOperation` is declared in `Data/AppDbContext.cs`; `CardPurchase` and `StatementAdjustment` are in `Finance/FinanceEntities.cs`. Add nullable category/merchant/source IDs only to entity types that use them, preserve current `Category` columns as the snapshot, and add workspace-composite FKs `(WorkspaceId,CatalogId)`.
- Seed exactly two categories per workspace: `Sin clasificar` with `Kind=Expense` and `Kind=Income`, including workspaces with no operations. Add one category for every distinct historical `Category` string/workspace/kind (exact case and spacing), using that exact string as `Name` and preserving every source row's snapshot byte-for-byte. Empty labels map to the seeded category of the known type. `FinanceMerchant` and `FinanceIncomeSource` remain empty until explicitly created. Operation kind derives from `FinanceOperation.Kind`; card purchase/adjustment is `Expense`. Never infer merchant, income source, or suggested business categories from description.

- New Personal and Household workspaces receive their two uncategorized rows in their existing creation transaction through `CatalogDefaults.AddForWorkspace(AppDbContext db, Guid workspaceId)`. Existing bootstrap/read of an already-created space does not write or duplicate rows. Historical whitespace-only labels map to the default without changing the original snapshot; other nonempty labels retain exact case and spacing.

- [ ] **Step 1: Write failing migration tests** `legacy_category_backfill_preserves_labels_and_is_workspace_scoped`, `case_and_spacing_distinct_legacy_buckets_are_preserved`, `uncategorized_expense_and_income_are_seeded_for_empty_workspace`, `empty_label_maps_to_uncategorized_category_of_known_kind`, `income_label_does_not_infer_source`, `merchants_and_income_sources_start_empty`, `card_purchase_and_adjustment_categories_are_preserved`, and `same_label_in_two_workspaces_creates_distinct_categories`, `new_personal_and_household_receive_defaults_atomically`, and `repeated_existing_workspace_bootstrap_does_not_add_catalog_rows` using synthetic values.
- [ ] **Step 2: Check RAM; run only `FinanceCatalogMigrationTests` through `work/with-ram-limit.sh`.** Expected RED: catalog tables/foreign keys absent.
- [ ] **Step 3: Implement additive schema and idempotent per-workspace backfill.** Preserve all existing IDs, money, dates and category strings.
- [ ] **Step 4: Rerun filtered migration tests via the wrapper.** Expected GREEN: history remains unchanged and workspace IDs differ.
- [ ] **Step 5: Commit** (`feat: add workspace finance catalogs`).

### Task 2: Versioned catalog endpoints and permission mapping

**Files:**
- Create: `src/EnigmaBudget.Api/Finance/CatalogEndpoints.cs`, `CatalogService.cs`
- Modify: `src/EnigmaBudget.Api/Finance/FinanceEndpoints.cs`, `Contracts.cs`, `src/EnigmaBudget.Api/Workspaces/FinanceWorkspaceFilter.cs`
- Test: `tests/EnigmaBudget.Api.Tests/FinanceCatalogIntegrationTests.cs`, `WorkspaceAuthorizationTests.cs`
- Modify: `src/EnigmaBudget.Api/Finance/CatalogEntities.cs`, `CatalogDefaults.cs`, `src/EnigmaBudget.Api/Data/AppDbContext.cs`, `src/EnigmaBudget.Api/Program.cs`
- Generate additive EF migration `CatalogCommandsAndDefaults` plus designer/model snapshot

**Interfaces:**
- `GET /api/finance/catalog` returns selected-workspace categories, merchants and income sources.
- Routes for mutations: `/api/finance/catalog/categories`, `/merchants`, `/income-sources`, with `/{id}` for PUT/DELETE. Create `(Guid Id,string Name,string? Kind)` uses Id as resource/request ID; update `(Guid RequestId,int Version,string Name)` changes name only; archive DELETE `(Guid RequestId,int Version)` never deletes history. Category/income-source kind is immutable after creation. New names are trimmed, nonblank, at most 80 characters, and contain no control characters. Historical labels remain untouched.
- Add `CatalogCommand` keyed by `(WorkspaceId,RequestId)` with command kind, target resource ID, canonical raw-payload SHA256, stored result JSON, server actor ID and UTC timestamp, plus workspace/actor FKs. Hash before trimming so a changed payload conflicts. Preserve all existing checks and revalidate permissions under the workspace lock before replay; resource/audit/command are atomic.
- Add `FinanceCategory.IsDefault`; backfill only the seeded exact `Sin clasificar` rows, enforce unique `(WorkspaceId,Kind)` when IsDefault, and set it in `CatalogDefaults` for fresh spaces. Default category name/kind/archive state is protected (409); client input cannot set the flag. Other referenced entries may be archived without losing history. Expose IsDefault in category DTOs.

- `POST/PUT` for categories, merchants and income sources; `POST` uses GUID RequestId and stored canonical payload hash; `PUT` requires current `Version`. `DELETE` means archive. Archived referenced IDs remain valid for history.
- Route filter maps `GET` to `read`, all catalog mutations to `manageResources`; write handlers validate CSRF and perform workspace-scoped idempotency/version checks under `WorkspaceMutationGate`, re-resolving authorization inside the locked workspace. Workspace and actor always come from `FinanceWorkspaceFilter.Get(http)`.
- Exact trimmed, case-sensitive name uniqueness only within the workspace/kind. Cross-workspace lookups return 404; active incompatible category kind returns 400/409.

- [ ] **Step 1: Write failing API tests** `catalog_crud_requires_manage_resources`, `catalog_replay_compares_payload`, `catalog_edit_requires_current_version`, `archive_retains_historical_reference`, `same_name_conflicts_only_inside_workspace`, `foreign_catalog_id_is_not_found_and_shared_reader_is_read_only`, and `category_kind_is_validated`, `default_category_cannot_be_renamed_or_archived`, `catalog_kind_is_immutable`, `catalog_replay_revalidates_permission_after_workspace_lock`, `catalog_request_id_cannot_be_reused_for_another_resource_kind`, and `catalog_mutation_and_audit_are_atomic`.
- [ ] **Step 2: Check RAM; run only the filtered catalog and authorization integration tests via wrapper.** Expected RED: routes and permission mapping are missing.
- [ ] **Step 3: Implement workspace-scoped catalog CRUD/archive and typed DTOs.** Trim new user-entered names once at the write boundary; compare and enforce uniqueness on the exact stored value, case- and space-sensitive. Do not normalize or rewrite historical label buckets.
- [ ] **Step 4: Rerun filtered tests.** Expected GREEN: versions, permissions, replay and isolation hold.
- [ ] **Step 5: Commit** (`feat: manage finance catalogs`).

### Task 3: Attach catalog IDs to operations and purchases

**Files:**
- Modify: `src/EnigmaBudget.Api/Finance/Contracts.cs`, `CashService.cs`, `CardService.cs`, `FinanceQueries.cs`
- Modify: `web/src/app/core/api.models.ts`, `api.service.ts`, `web/src/app/finance/operations.ts`, `operations.html`
- Test: `tests/EnigmaBudget.Api.Tests/FinanceCatalogIntegrationTests.cs`, `web/src/app/finance/finance.spec.ts`

**Interfaces:**
- Add optional `CategoryId`, `MerchantId`, and `IncomeSourceId` to `OperationCreate`; category and merchant IDs to `PurchaseCreate`; category ID to adjustment input. Keep existing category string and expose additive ID/display-snapshot fields on read DTOs. `OperationCreate.AccountId` is currently required (`Guid`), and `OperationDto.AccountId` is also required; this plan does not make either nullable.
- Validate each foreign key within selected workspace. Category kind must match operation kind; income source is allowed only on Income. Backend writes catalog snapshot from resolved workspace row. The new UI requires a category for a new income/expense and source for income; legacy callers may still send category text, resolved only by exact workspace match.
- Renaming any catalog value does not alter `FinanceOperation`, `CardPurchase`, or adjustment snapshots.

- [ ] **Step 1: Write failing tests/specs** `operation_category_and_source_ids_match_workspace_and_kind`, `purchase_merchant_and_category_snapshot_survive_rename`, `legacy_string_resolves_without_fuzzy_match`, `income_source_is_only_valid_for_income`, `structured_selector_preserves_draft_on_failure`, and `archived_catalog_item_is_not_offered_for_new_entry`.
- [ ] **Step 2: Check RAM; run filtered .NET tests then focused finance Vitest sequentially via wrappers.** Expected RED: request DTOs and form selectors lack IDs.
- [ ] **Step 3: Implement scoped resolution/snapshots and structured selectors.** Do not alter ledger, cash payment or card-payment behavior.
- [ ] **Step 4: Rerun both filtered suites sequentially.** Expected GREEN: compatibility, snapshot and draft-retention assertions pass.
- [ ] **Step 5: Commit** (`feat: classify finance records`).

### Task 4: Monthly category budgets and source rows

**Files:**
- Create: `src/EnigmaBudget.Api/Finance/BudgetEntities.cs`, `BudgetService.cs`
- Modify: `src/EnigmaBudget.Api/Data/AppDbContext.cs`, `FinanceEndpoints.cs`, `Contracts.cs`, `FinanceWorkspaceFilter.cs`
- Generate EF migration `MonthlyFinanceBudgets` plus its generated designer/model snapshot
- Test: `tests/EnigmaBudget.Api.Tests/MonthlyBudgetIntegrationTests.cs`, `CardCycleIntegrationTests.cs`

**Interfaces:**
- `MonthlyBudget(Guid Id,Guid WorkspaceId,string Period,Guid CategoryId,string Currency,decimal Limit,int Version)` is unique by workspace/period/category/currency. `BudgetService.GetAsync(WorkspaceRequestContext context,string period,string currency,CancellationToken ct)` returns `(IReadOnlyList<BudgetCategoryRowDto>? Result,int Status,string? Error)`. `BudgetCategoryRowDto(Guid CategoryId,string CategoryName,string Currency,string LimitAmount,string SpentAmount,string CommittedCardAmount,string RemainingAmount,IReadOnlyList<BudgetSourceRowDto> Sources)` and `BudgetSourceRowDto(string SourceKind,Guid SourceId,string? ComponentId,string Date,string Description,string Amount)` expose all monetary values as decimal strings. API `GET /api/budgets?period=YYYY-MM&currency=ARS|USD` returns that list. `POST /api/budgets` accepts `MonthlyBudgetCreate(Guid Id,string Period,Guid CategoryId,string Currency,string LimitAmount)`; `PUT /api/budgets/{id}` accepts `MonthlyBudgetUpdate(Guid RequestId,int Version,string LimitAmount)`; both validate CSRF and use `manageResources` with payload replay/version checks.
- In this standalone catalog release, `FinanceQueries.FinanceOverview` is the source seam: at baseline the legacy `budget.cashExpensesArs/Usd` sums nonvoid Expense `FinanceOperation.Amount`, while `budget.cardInstallmentsArs/Usd` sums `CardInstallment.Amount` for the period and confirmed statement adjustments are separately exposed as card charges. Add `budget.operationExpensesArs/Usd` with exactly the same value as the legacy fields, computed once and aliased in the response. The first `BudgetService` uses each nonvoid Expense `Amount` in its operation period, each canonical installment in its actual period, and each confirmed adjustment once; it ignores `CardPurchase.TotalAmount`, `CardStatement.ActualTotal`, and `CardPaymentRecord` as expenses. Unclassified remains visible. When Recurring/Healthcare is integrated later, it changes the single operation-row contribution source to `OperationSettlementCalculator.Calculate(...).OperationMonthBudgetContribution`; that calculator is defined there, not assumed to exist for this standalone release.
- `RemainingAmount = Limit − SpentAmount − CommittedCardAmount`, unclamped. Final integrated behavior is: gross 1,000 with payroll 300 + bank 700 counts 1,000 in the operation month; gross 1,000 with payroll 300 + card 700 counts 300 in the operation month and 700 in the actual installment month. The Recurring/Healthcare plan supplies this through the shared decimal calculator; a voided operation contributes zero while its real linked installment remains.

- [ ] **Step 1: Write failing tests** `budget_requires_expense_category_and_valid_currency`, `budget_write_requires_manage_resources`, `budget_read_is_workspace_scoped`, `period_validation_is_strict`, `monthly_budget_counts_cash_expense_and_card_installment_once`, `payment_and_statement_total_do_not_count_as_expense`, `overview_operation_expenses_alias_equals_legacy_value`, `uncategorized_expense_remains_visible`, `ars_and_usd_are_never_combined`, `partial_operation_payment_does_not_change_budget_spend`, and `changed_budget_version_conflicts`.
- [ ] **Step 2: Check RAM; run only `MonthlyBudgetIntegrationTests|CardCycleIntegrationTests` through wrapper.** Expected RED: budget endpoints and aggregator absent.
- [ ] **Step 3: Implement versioned budget CRUD and read-only aggregation.** In this plan's baseline, feed nonvoid Expense `Amount`, canonical installments and confirmed adjustments into one explainable query. Add `operationExpensesArs/Usd` to `FinanceOverview.budget`; set legacy `cashExpensesArs/Usd` from the same computed values, not a second sum. Preserve this contract so the later Recurring/Healthcare plan replaces only the operation contribution source with `OperationMonthBudgetContribution`.
- [ ] **Step 4: Rerun filtered tests.** Expected GREEN: each consumption source appears once and periods/currencies isolate.
- [ ] **Step 5: Commit** (`feat: add monthly category budgets`).

### Task 5: Catalog and budget screens

**Files:**
- Create: `web/src/app/finance/catalog.ts`, `.html`, `.css`, `.spec.ts`; `budgets.ts`, `.html`, `.css`, `.spec.ts`
- Modify: `web/src/app/core/api.models.ts`, `api.service.ts`, `web/src/app/finance/finance-shell.ts`, `.html`, `.css`
- Test: `web/src/app/finance/finance.spec.ts`

**Interfaces:**
- Typed `ApiService` methods use active workspace and existing `financeMutate` for CSRF, idempotency, busy state, error and draft retention. Add Spanish navigation for Categorías y comercios, Fuentes de ingresos and Presupuesto.
- Reader can see but not change; archived entries appear for linked history, never as selector options. Budget detail labels the monthly amount “Consumo registrado”, labels actual account movement “Caja”, lists category, cap, operation contribution, card installments and remaining cap, links source rows, and keeps currencies distinct.

- [ ] **Step 1: Write focused Vitest specs** for paths/workspace headers, permissions, failed-form draft retention, archive selector state, source detail, Spanish labels, currency filter and responsive component state. Vitest does not certify screenshot overflow.
- [ ] **Step 2: Check RAM; run focused Vitest through wrapper with Node heap ≤2 GiB and ≤2 workers.** Expected RED: APIs/components/nav absent.
- [ ] **Step 3: Implement the views with the existing finance shell and dialog patterns.** Clear old query data and drafts on workspace change.
- [ ] **Step 4: Rerun focused Vitest through wrapper.** Expected GREEN: permission, responsive and source-trace assertions pass.
- [ ] **Step 5: Commit** (`feat: add catalog and budget screens`).

### Task 6: Live API E2E and Product Design responsive QA

**Files:**
- Create: `web/e2e/catalog-budget.spec.ts`
- Test: `web/e2e/catalog-budget.spec.ts` using the existing Playwright webServer and a task-owned disposable PostgreSQL database with current migrations and synthetic test identities/workspaces
- QA evidence (ignored): `work/qa/catalog-budget/` screenshots and visual findings

**Interfaces:**
- Reuse `web/playwright.config.ts` webServer and its explicit test-connection/webroot settings; do not start a second host. Controller/fixture provisions one owned disposable database on55432 with current migrations and synthetic identity/workspace/membership and permissions, then disposes only owned resources. Verify HTTP-served index matches the final compiled artifact. Manual IAB QA uses localhost on its owned port to isolate cookies from user127.0.0.1:5082. Playwright uses the served Angular app and real API with no API mocks; select workspaces through the app so its established `X-Workspace-Id` context is exercised. Never use preview5082 or user database55433.
- Named E2E cases: `catalog_budget_live_api_round_trip_and_category_sources`, `live_budget_shows_operation_and_installment_once`, `reader_can_view_catalog_without_mutation`, and `workspace_switch_discards_old_catalog_and_budget`. Verify the API persisted category/budget/operation/card source rows and that the UI renders Spanish source details, permission state, currency separation and the compatibility fields once.
- Capture actual rendered pages at 360×800, 390×844 and 1280×800 and review those screenshots with the Product Design responsive-audit workflow for clipped controls, horizontal overflow, unreadable tables and lost actions. Vitest/Playwright functional assertions do not claim to prove pixel overflow; the screenshots and visual review are the evidence.
- Keep screenshots, E2E logs and findings under ignored `work/qa/catalog-budget/`. This task does not publish or commit release evidence; only the controller reviews those artifacts and decides any root-level release evidence.

- [ ] **Step 1: Write the live API Playwright cases** for CRUD/read-only/workspace-switch and the operation-versus-installment month scenario. Assert persisted API responses and visible Spanish content; do not add screenshot-overflow assertions.
- [ ] **Step 2: Check RAM, confirm ≥6 GiB available, start only the existing app and isolated PostgreSQL fixture, and run Playwright with one worker through the memory wrapper.** Report the actual first result honestly: Tasks1–5 already provide endpoints/screens, so initial GREEN is valid; only real failures justify integration fixes.
- [ ] **Step 3: Run the live API E2E suite sequentially.** Expected GREEN: the app, API, database and workspace authorization agree on the persisted source rows.
- [ ] **Step 4: Capture the three specified viewport screenshots and review them visually with the Product Design audit workflow.** Save the images and concise findings only under ignored `work/qa/catalog-budget/`; fix only integration/visual defects in the owned source files, run covering checks and rebuild, then recapture.
- [ ] **Step 5: Hand the ignored E2E/screenshot evidence paths to the controller for root-only release review.** Do not publish evidence or claim a release from this task. Commit the assigned E2E/source fixes, never ignored evidence; report exact source/artifact hashes, actual runs and owned resources.

## Self-review

- **Coverage:** historical/empty-workspace categories and new classified records → Tasks 1–3; commerce/source catalogs remain user-created; budgets/ARS-USD/anti-double-count → Tasks 4–5; live API behavior and screenshot-reviewed sizes → Task 6.
- **Types:** catalog migration seeds only the two uncategorized types plus every exact historical label; merchants/sources start empty. Catalog-only budget reads gross active Expense and canonical installments; future Recurring/Healthcare integration replaces its operation contribution source with the calculator defined there. `operationExpensesArs/Usd` and retained `cashExpensesArs/Usd` aliases share one result. UI calls it “Consumo registrado” and separates “Caja”.
- **Review focus:** each risky input is pinned to a named test in the task that owns its data.
- **Proportion:** no recurring behavior or general rule engine is included in this standalone plan.

