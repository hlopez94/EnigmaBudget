# Product Design refresh Implementation Plan

> Use superpowers:subagent-driven-development with gpt-6-luna, one implementer at a time. The user explicitly delegated option selection and autonomous execution; do not ask for design approval.

**Goal:** Implement the selected «Panorama sereno» visual direction in the real application and verify its finance flows and responsive fidelity.
**Architecture:** Keep the existing Angular22/OptimusUI2 frontend and single.NET10/PostgreSQLhost. Change shared tokens/layout and focused templates, not financial contracts or data. Use Product Design image-to-code/design-qa on existing code, not a new starter.
**Spec:** docs/superpowers/specs/2026-10-25-product-design-refresh.md; source visual work/product-design/selected-panorama.png.

## Global Constraints

- Preserve the complete app goal, user data, real API behavior, permissions, decimal-string amounts and ARS/USD separation. Spanish copy. No Ionic/new runtime.
- All agents Luna. One code/heavy worker; availableRAM>=6GiB, work/with-ram-limit.sh4GiB/512MiBswap, Node2GiB/AngularVitest2/Playwright1. TestPG55432 only; previewPG55433 never fixtures/reset.
- Product Design selection delegated explicitly; use existing brand and Optimus controls, no approval menus. New icon library must be verified/pinned from official documentation.
- Keep webroot isolated during test/build. No backend restart/migration for frontend-only work; sync additive assets/indexlast only after gates.

## Review Focus

- Incomplete setup, long workspace labels and 360px must not push the primary finance action out of reach or overlap controls.
- Very large/negative ARS/USD values and credit balances remain readable and semantically correct.
- Reader/archived contexts keep allowed reads and hide writes; draft/focus/busy behavior must survive layout changes.
- No Unicode pseudo-icons, nested-panel clutter or new global CSS leakage into Optimus dialogs.
- All original monetary/idempotency/multiuser browser assertions remain; tests may adjust selectors only for intentional accessible markup changes.

## Task 1: Implement the selected application visual system

**Files:** web/src/styles.css; web/src/app/app.html and existing app styles; finance/finance-shell.{html,css,ts}; focused existing accounts/cards/operations/sharing/workspaces/onboarding templates/styles only as needed. web/package.json and lock only for selected icon library. Tests existing frontend specs and web/e2e/*.spec.ts.
**Interfaces:** All existing ApiService/data models and permission inputs stay. Shared CSS tokens feed surfaces/type/spacing/statuses. Existing dialogs remain OptimusUI; icon wrapper/library is presentational with accessible labels. No new endpoint/data claims.

- [ ] Inspect the selected image, current screenshots, source tokens/components and full spec; map visible elements to existing live data. Check official icon-library compatibility before installing.
- [ ] Add meaningful failing frontend/browser regressions for mobile hierarchy and large-value/long-space geometry, preserving original tests; verify RED before layout implementation.
- [ ] Implement home hierarchy and responsive shared shell/system across operational screens. Compact setup assistance; ARS/USD separate and card debt distinguished from monthly quotas. Maintain other finance actions, read-only and draft/state behavior. Replace pseudo-icons with coherent library icons.
- [ ] Fix misleading invitation copy so an already-active member is not promised a new role; use honest conditional-neutral wording without weakening server authority.
- [ ] Run affected/full frontend tests, production build and all real browser cases sequentially under caps, capture 360/390mobile and1280desktop with synthetic test identities. No API full rerun unless backend changes (not planned).
- [ ] Commit source/tests/docs only; private report in own SDD workspace, no force-add. Do not sync preview before controller's task review. Return SHA, commands/output and screenshots.

## Task 2: Product Design fidelity QA and safe visual delivery

**Files:** project-root design-qa.md, docs/producto/estado-implementacion.md; screenshots/comparison boards under ignored work/product-design/.
**Interfaces:** Source selected image and implementation captures represent same viewport/content state. Normalize density and juxtapose into one comparison image; inspect primary and focused regions. Use in-app browser for actual visual/interactions check; existing automated regression suite remains authorized.

- [ ] Read Product Design design-qa skill/rubric; open source and rendered screenshots, compose normalized comparison, assess all five fidelity surfaces.
- [ ] Record any P0/P1/P2, fix scoped source through same sole implementer, recapture/recompare until passed; P3 can be documented. No source screenshot rendered as page, fake API or visual-only finance controls.
- [ ] Verify visible interactions in local browser: sections, primary dialog, focus/close, form labels, Reader/archived state and setup return; preserve user session/data, use isolated synthetic evidence for writes.
- [ ] After source gates/review/QA, safely sync verified assets to5082, retaining prior hashes/indexlast; HTTPhealth/assets and actual browser appearance checked. No forced reload of user draft tabs.
- [ ] Update tracker/report with real evidence and remaining full-app modules; no claim of app completion. Push same draftPR4 only at controller instruction.
