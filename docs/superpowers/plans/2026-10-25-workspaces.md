# Workspaces, household and privacy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development with Luna, one implementer at a time. User authorized the complete application; proceed continuously.

**Goal:** Add real Personal/Hogar membership, server-side resource permissions, invitations and opt-in personal reading without changing existing financial history.

**Architecture:** Preserve old workspace IDs, add users/memberships and a shared resource authorization context. Existing financial handlers consume that context; Google/cookie/CSRF remains. UI selects authorized spaces and reacts to permission/privacy changes; no additional identity provider or backend.

**Tech Stack:** .NET10/EF/PostgreSQL, Angular22/OptimusUI2, xUnit/Vitest/Playwright.

**Spec:** docs/superpowers/specs/2026-10-25-workspaces-design.md; exact DTO/routes/permissions in docs/api/espacios.md; full objective docs/producto/estado-implementacion.md. Read-only builder check found no load-bearing blocker; user confirmed Hogar includes four personal feature sets plus common space.

## Global Constraints

- Same feature branch/PR4, no main merge, no private data/secrets in git. Preserve every existing Workspace/account/card/ledger/source ID and amount/date.
- User/role/permission from server; selection header not authority. Personal private by default; read grant never implies write, group membership or commercial entitlement.
- Every financial list/detail/mutation uses authorized selected workspace; no approval filter followed by old OwnerSubject lookup.
- Invites reserve up to four total places, expire7days,256bit token/hash-only, verified recipient, atomic limit check and no messages to real third parties during tests.
- Existing cookie/Google/CSRF/no-store/Production fail-closed policies remain. No Auth0 or other provider migration.
- All agents Luna; heavy strictly sequential, direct RAM wrapper4Gi/512Mi, refuse<6Gi available, dotnetmaxcpu1/no compiler server, Node2Gi/Angular/Vitest2/Playwright1.
- Preview5082/PG55433 is user data and must never be reset/seeded. TestPG55432 only disposable DB/schema; don't stop unrelated apps.

## Review Focus

- Migration preserves exact old personal views and IDs, and concurrent first sign-in creates one user/personal/owner membership.
- Forged/stale workspace header, resource IDs and Reader writes fail without financial effects or silent context fallback.
- Family Admin cannot view another member's Personal before consent; grant permits only read, revocation/leave cuts subsequent access and rejoin does not restore consent.
- Concurrent invitations/acceptances never exceed four seats, and revoked/used/wrong-recipient tokens cannot revive membership.
- Switching spaces discards drafts only after explicit choice, clears prior data, and late responses never populate another context; hidden actions do not replace server checks.

## Task 1: Additive membership and financial authorization core

**Files:** Create focused Workspaces/{WorkspaceEntities,WorkspaceContracts,WorkspacePermissions,WorkspaceAccessService,FinanceWorkspaceFilter,WorkspaceEndpoints,WorkspaceAuditInterceptor}.cs and Identity/WorkspaceProvisioner.cs (decompose further when appropriate). Modify Data/AppDbContext.cs, Program.cs, Finance helper and scope callsites; additive EF migration/snapshot. Tests WorkspaceMigrationTests, WorkspaceAuthorizationTests, WorkspaceIntegrationTests plus small ApiFactory extensions.

**Interfaces:** Shared WorkspaceProvisioner.EnsurePersonalAsync resolves stable current subject into user+permanent Personal. WorkspaceAccessService resolves authenticated principal + optional selected UUID + required permission into WorkspaceRequestContext(user,workspace,role,permission set). FinanceWorkspaceFilter stores that context; all financial handlers consume it. Public DTO/route names in docs/api/espacios.md. Existing onboarding always Personal, legacy clients no header still Personal.

- [ ] Add migration regression starting from old schema/real legacy rows and prove exact IDs, balances, debt, opening dates/history survive upgrade; two-user default spaces remain isolated. Add per-host multi-identity setup; capture meaningful RED before implementation.
- [ ] Add normalized user/Kind/membership/core audit, backfill oldOwnerSubject users/Owner links idempotently; nullable unique OwnerSubject only for non-Personal. Keep financial FKs, no reposting/money migration.
- [ ] Implement shared provisioning/collision handling and authenticated workspace list/create/update, members/role/leave/owner transfer, me and safe audit. Four-member role limit checked server-side; no commercial-price or billing activation invented.
- [ ] Map each financial endpoint to exact permission table and replace owner-only scope queries with authorized request workspace. Central read/write authorization, CSRF unchanged; audit in same financial transactions (e.g. scoped SaveChanges interceptor), no nested transaction wrapper around existing serializable mutations. Secrets/tokens excluded from audit.
- [ ] Test Owner/Admin/Member/Reader differences, resource404 and wrongcontext, readonly writes403, archive/owner protections, simultaneous provisioning. Run focused then full backend Release, commit backend/tests only and ignored task-1-report.md with exact results/SHA/route deviations.

## Task 2: Invites, verified identities and personal read grants

**Files:** Create Workspaces/{InvitationService,PrivacyGrantService}.cs plus focused contracts/entities/migration/tests; Identity/GoogleIdentityClaims.cs and development identity options/mapper. Extend shared access service. No frontend work.

**Interfaces:** Consumes user/membership/context/core audit. Produces all invitation/personal-sharing endpoints from docs/api/espacios.md and dynamic read-grant entries in GETworkspaces. Google profile trusted-claim enrichment only; existing Google handler/session compatibility preserved. Development extra-user configuration strictly unavailable outside Development.

- [ ] Add focused failing tests: before consent foreignPersonal404 even for householdAdmin; after consent readonly view with writes403, groupdetails403 and no group/participant arrays in overview; revoke/leave/rejoin behavior; missing-CSRF, subject spoofing and old signed-in profile without verified email.
- [ ] Implement256bit hash-only invitation,7day UTC expiry, pendingseat reservation, recipient verification, single-use/idempotent acceptance and role/capacity transitions atomically. Lost first shareUrl has explicit recovery via revoke/new; no plaintext token persisted/logged. Test conflicting and wrong-recipient/expired/revoked/reused links and racing capacity.
- [ ] Implement explicit own-Personal grants constrained to active Household memberships on both sides; deactivate on owner leaving and don't silently restore at rejoin. All financial reads use grant-based Reader, never house-role write permissions.
- [ ] Add Google email-verification claim from actual trusted raw profile; test true/false/missing fields. Add allowlisted synthetic dev users only behind existing opt-in/Development guard for browser testing, preserve default local identity. Verify Production404 despite malicious/configured request fields.
- [ ] Run focused/full backend once appropriate, commit backend/tests only and report task-2-report.md with security/migration evidence and configuration limitations. No email delivery or real-user messaging.

## Task 3: Space selection and household/privacy management UI

**Files:** Extend core models/ApiService, root session/context state and finance shell; create focused workspace picker/members/invites/personal-sharing components using mobile dialog pattern. Integrate permission inputs in existing feature actions; add frontend behavior tests.

**Interfaces:** Consume docs/api/espacios.md unchanged. Selection stored per tab/user as preference, never finance data; header applied at dispatch only to financial calls. Auth/onboarding/me/workspace management use their explicit server scope. Permissions/data/context updated together.

- [ ] Build mobile-safe space picker and context label, Household creation/list/members/invitations/roles/leave/transfer as permitted, copy-once invite link and signed-in acceptance workflow with verified-recipient errors. Keep tokens out of URL query/server logs; preserve pending link across Google login without external messaging.
- [ ] Add personal consent UI with private default, revocation and explanation of copies already seen/exported. Reader contexts show no write forms/buttons; existing financial methods still server-protected.
- [ ] Handle switch with pending draft confirmation/cancel, dispose old-context forms/cache, and reject stale responses captured for another context. Logout clears context/private memory. Add deferred response tests and authenticated context/permissions interactions.
- [ ] Run frontend tests/build under cap, commit UI and report task-3-report.md. No fake paid-plan UI or provider-success claims.

## Task 4: Real multi-user acceptance and safe local delivery

**Files:** Add web/e2e/workspace-privacy.spec.ts, explicit synthetic dev profile config in test server only, status/docs and reproduced integration fixes. Preserve existing3 finance/mobile acceptance cases and monetary assertions.

- [ ] Real host/PostgreSQL/browser with isolated Alice/Bob/Reader contexts: create shared Household, invite/accept, record shared expense, prove personal unchanged/private; optional Personal reading, denied write, revocation/leave/rejoin. Require visible real actions and actual403/404, no mocked API responses.
- [ ] Exercise four-seat pending/active cap with backend concurrency and browser capacity feedback, archived/owner protections, no old-context data flash/draft posting. Retain phone360/390 and desktop plus full existing finance flow.
- [ ] Before applying the additive migration to the user preview, create a private ignored PostgreSQL backup with restricted file permissions, verify it by restoring into an explicitly owned disposable test database, and keep the original preview database untouched during the restore check. Never commit backup contents or print user data.
- [ ] Run required full integration/frontend/browser gates sequentially, publish separate output and safely update only own5082 server with same original contentroot/auth; additive static assets, preserve PG/userdata/no forced reload. Update tracker with actual evidence, remaining commercial/group/cash modules pending.
- [ ] Commit tests/docs, review, push same draft PR and inspectCI. Global goal remains active: next savings/projection and household/group financial circuits, not a declaration that whole app is done.
