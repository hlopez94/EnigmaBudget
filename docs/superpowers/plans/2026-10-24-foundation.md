# EnigmaBudget Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking. User explicitly authorized starting implementation with Luna agents; preserve that execution method without another permission request.

**Goal:** Deliver a running first vertical slice: Google/development sign-in, isolated personal workspace, resumable bank/card setup, persisted opening balances in ARS/USD, and a responsive Angular UI.

**Architecture:** One ASP.NET Core 10 host, EF Core/Npgsql and PostgreSQL. The host serves the compiled Angular 22 application and its same-origin cookie-authenticated API. This initial slice does not pretend to implement future product modules.

**Tech Stack:** .NET 10, PostgreSQL 17, Angular 22, OptimusUI 2, Vitest, xUnit, Playwright; no Ionic. S3-compatible local storage and R2 configuration in deployment preparation.

**Spec:** docs/superpowers/specs/2026-10-24-finanzas-design.md

## Global Constraints

- Angular 22 and OptimusUI 2 compatible with Angular 22; .NET 10; PostgreSQL.
- One ASP.NET host serves API and compiled frontend; no separate backend services and no Ionic.
- All implementers and reviewers use gpt-6-luna per explicit user request.
- Spanish UI. API money uses invariant decimal strings, persisted as PostgreSQL numeric(18,2); ARS/USD remain separate.
- Never publish source spreadsheet links, personal financial data or secrets.
- No product code copied from old branches; keep main unchanged and work on codex/rebuild-net10-angular22.
- Authorization is enforced in the server using authenticated identity and membership, not a client-supplied tenant.
- The assistant records opening balances, not income; card statement debt and future installment commitments are separate.
- Initial slice is personal onboarding only; household/groups/plans/RBAC expansion, transactions, forecasts, DolarAPI, attachments, importers, offline sync and subscriptions remain explicitly on the roadmap.

## Review Focus

- User B cannot see or overwrite user A's onboarding or confirmed accounts, even with guessed IDs: Task 1 integration test.
- Duplicate/retried confirmation cannot duplicate openings or accounts: Task 1 integration test.
- Decimal ARS/USD amounts are not mixed or rounded by browser floating arithmetic: Tasks 1 and 2 validation tests.
- Stale draft versions and partially completed forms survive reload without silent overwrite: Tasks 1 and 2 tests.
- Production cannot enable developer impersonation or accept a mutation without CSRF protection: Tasks 1 and 3 tests/checks.

## Contract

Define DTOs in src/EnigmaBudget.Api/Onboarding/Contracts.cs and matching types in web/src/app/core/api.models.ts.

- GET /api/health: anonymous liveness; no secrets.
- GET /api/auth/session: anonymous { authenticated: boolean, displayName: string|null, googleConfigured: boolean, developmentLoginEnabled: boolean, csrfToken: string }. Issues antiforgery cookie and token; authenticated request provisions/returns own personal workspace.
- GET /api/auth/google: OAuth challenge, or 503 when unconfigured. Safe fixed local return destination; no arbitrary redirect parameter.
- POST /api/auth/development: development-only explicit opt-in login. Header X-CSRF-TOKEN required. Uses one fixed local test identity, never accepts a client-supplied identity; not available in production.
- POST /api/auth/logout: CSRF protected; sign out.
- GET /api/onboarding: authenticated { version: integer, completed: boolean, accounts: AccountInput[], cards: CardInput[] }. Draft initial version=0. Scope derived from identity.
- PUT /api/onboarding: CSRF protected SaveDraftRequest {version,accounts,cards}; persists incomplete drafts; returns full state with incremented version; stale version ->409.
- POST /api/onboarding/complete: CSRF protected {version}; validates saved draft and atomically creates accounts/cards/opening records; returns full state completed=true. Repeated confirmation returns existing completed state and never duplicates. Zero resources allowed (explicit skip). No editing after completion in this slice (409).
- GET /api/accounts: authenticated list of confirmed account summaries (including currency, openingBalance, openingDate).
- GET /api/cards: authenticated list of confirmed card summaries (including statementDebtArs/Usd, asOfDate, closingDay, dueDay).
- Validation errors use ProblemDetails, 400; unauthenticated API ->401, forbidden ->403, stale/completed draft ->409.
- AccountInput {id: UUID,name:string,bankName:string,currency:'ARS'|'USD',openingBalance:string,openingDate:'YYYY-MM-DD'}.
- CardInput {id: UUID,name:string,bankName:string,closingDay:integer|null,dueDay:integer|null,statementDebtArs:string,statementDebtUsd:string,asOfDate:'YYYY-MM-DD'}.
- Partial drafts may contain blank fields, but sizes and resource counts are bounded. Completion requires names/dates/currencies, finite invariant amounts <=2 decimal places, amounts within numeric(18,2), valid calendar dates and days 1..31; debts nonnegative, account balances may be negative. IDs must be unique. Dates are date-only (no timezone shift). Limits: 50 accounts and 50 cards, text <=120 characters.
- Draft uses string amounts to preserve partial typing. Completion parses in invariant culture to decimal; confirmed balance JSON remains strings.
- Initial UI explicitly labels cards as current statement debt; manual future-installment management is subsequent work, with roadmap copy rather than fake controls.

## Task 1: Backend, identity, PostgreSQL and onboarding

**Files:** Create EnigmaBudget.sln, global.json, src/EnigmaBudget.Api/{EnigmaBudget.Api.csproj,Program.cs,appsettings.json}, Identity/, Data/, Onboarding/, Accounts/, tests/EnigmaBudget.Api.Tests/. Keep focused files rather than one enormous Program.cs. Create tests README if external PostgreSQL needed.

**Interfaces:** Consumes Contract above. Produces its API, EF migrations and a single runnable .NET host; task 2 consumes JSON unchanged. .NET SDK is available at work/tooling/dotnet/dotnet if not on PATH. Local PostgreSQL: Host=127.0.0.1;Port=55432;Database=enigmabudget;Username=enigma;Password=local-development-only. Only local configuration/tests may use that development password. Production requires environment configuration.

- [ ] Step 1: Create project/test scaffolding and meaningful failing integration tests: unauthenticated state is 401; two authenticated test identities get isolated drafts; GET initial state has version 0; saving increments version and stale save returns409; completing valid ARS/USD accounts/cards preserves amounts and openings; repeat completion doesn't duplicate; invalid date/amount/currency/duplicate IDs gives400; production developer login returns404; mutations without valid CSRF return400. Test real PostgreSQL persistence with disposable test database/schema, not an in-memory substitute.
- [ ] Step 2: Run focused tests and record expected RED evidence before implementing behavior. Framework-generated scaffolding need not have tests.
- [ ] Step 3: Implement contract using EF Core 10/Npgsql, migrations and optimistic concurrency. Store draft and confirmed entities scoped to own workspace, unique Google subject mapping, opening balance records separate from income. Google uses official ASP.NET authentication handler, cookies and server-side callback; no extra hosted identity provider required. Opt-in dev auth only in Development; Google secret absent ->503. Secure cookie defaults, API401 rather than login redirects, antiforgery checks for mutations.
- [ ] Step 4: Run all dotnet tests and dotnet build with no errors. Inspect migrations and ensure stable project references. Document test command and external-service requirements. Do not falsely report live Google authorization tested without credentials.
- [ ] Step 5: Commit only this task's files. Write report with RED/GREEN evidence, commands/results, contract changes and limitations to provided report path.

## Task 2: Angular and OptimusUI responsive onboarding

**Files:** Create web/{package.json,angular.json,tsconfig*.json,proxy.conf.json}, web/src/{main.ts,index.html,styles.css}, web/src/app/{app.ts,app.html,app.css,app.config.ts}, core/{api.models.ts,api.service.ts}, onboarding/ and dashboard/. Test in relevant *.spec.ts and tests/ with Vitest. Generate Angular22 scaffolding then remove template content. Dependencies locked. Components split by feature/form when useful.

**Interfaces:** Consumes Task1 Contract unchanged. Browser calls /api on same origin, sends credentials and X-CSRF-TOKEN from refreshed session after login. UI must not invent unsupported endpoints or silently simulate persistence.

- [ ] Step 1: Write failing tests for form validation preserving decimal strings, negative card debt rejected, both currency accounts retained, draft restoration, stale-save409 displayed without dropping local edits, and submission error recovery. Test real validation logic/HTTP service, not template implementation details.
- [ ] Step 2: Run tests and capture RED before behavior code.
- [ ] Step 3: Build Spanish responsive UI with actual OptimusUI controls (Button, InputText, Select/Message or equivalent installed components), a calm polished visual system, useful empty states and keyboard labels. Sign-in screen explains setup and offers Google plus explicitly labeled developer sign-in only if session advertises it. Wizard: introduction, accounts, cards, review. Save/resume incomplete draft via API; Back/Next preserves form, explicit skip allows zero accounts/cards; confirmation uses latest saved version. Dashboard shows separate ARS/USD opening totals and configured cards (debt separately), meaningful welcome, and clear next steps. No fictional financial data and no fake feature navigation. Logout accessible. Responsive <=360px and desktop, no overflowing forms.
- [ ] Step 4: Run npm test -- --run and npm run build. Confirm Angular22 and OptimusUI2 actually installed; no Ionic. Ensure build output can be served under ASP.NET wwwroot. Explain which artifact integration task copies.
- [ ] Step 5: Commit task files and report RED/GREEN, commands and output paths. No SDK/build caches in Git.

## Task 3: Single-host packaging, local dependencies and browser verification

**Files:** Create Dockerfile, compose.yaml, .env.example, .dockerignore, .github/workflows/ci.yml, README.md, scripts/{build.sh,verify.sh}, web/e2e/onboarding.spec.ts, web/playwright.config.ts; modify src/EnigmaBudget.Api/Program.cs for safe SPA/static serving and verified transport/precision integration defects; extend backend integration tests accordingly. Add infra docs for S3/R2 environment settings and roadmap status. Implement storage options/config validation seam only; attachment endpoints are future scope.

**Interfaces:** Consumes backend/project paths and frontend dist output from task2. Runtime serves compiled Angular and /api within same host. Database and S3-compatible object service are infrastructure dependencies, not application microservices. Publish profile must not embed secrets or dev identity.

- [ ] Step 1: Add browser regression test: launch local opt-in developer session, create ARS account and USD account/card debt, save draft, reload, finish once, reload dashboard and assert separate balances/no duplicates. Assert failure409 behavior or validation where practical. Start from an empty isolated test DB/test identity. Verify backend still rejects unauthorized/missing-CSRF calls; no full bank card details requested.
- [ ] Step 2: Execute test against integrated single host; fix actual integration failures test-first where relevant. Add a regression that completion rejects a lexical amount with more than two fractional digits, including 0.00000000000000000000000000001 (decimal.TryParse rounds it to zero before the current numerical scale check). Validate lexical scale before parsing. Add actual HTTP Development session and HTTPS Production secure-cookie checks; secure production settings cannot be weakened for local convenience. Use bundled browser/Playwright if available; install local browser dependencies if necessary, no remote deployment.
- [ ] Step 3: Multi-stage image builds Angular22 then publishes .NET10 with static files. Local compose includes PostgreSQL17 and compatible S3 service (MinIO or equivalent maintained option) with private ports; credentials placeholders require environment values. Provide .env.example with local-only examples clearly labeled, secrets never committed. Storage options expose endpoint/bucket/region/ForcePathStyle and R2 configuration notes, not a fake implemented upload feature. CI builds/tests backend and frontend with PostgreSQL service, without requiring user secrets. Google configuration documented; missing credentials fail closed. SPA fallback must not turn /api typo into HTML200.
- [ ] Step 4: Run scripts/verify.sh (backend suite, frontend tests/build), integrated Playwright and container build/compose validation when available. Check Git diff for secrets/private source links and stale legacy workflows. README describes actual completion and commands, roadmap lists all unimplemented modules including household/groups/subscriptions, import and offline. Clearly distinguish opt-in local auth from real Google credentials needed for live login.
- [ ] Step 5: Commit; full report with exact evidence and residual limitations. Controller performs final review, pushes feature branch and creates draft PR without merging.

## Completion boundary

This plan starts implementation with a real executable foundation, as the user requested. It does not claim completion of the full SaaS. Subsequent plans cover transaction ledger, quotas/statements, household and groups, forecasts/goals, Excel migration, DolarAPI, file upload endpoints, Android/offline, subscriptions and administration. Do not scaffold fake completed screens for those modules.
