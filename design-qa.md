# Product Design QA — Panorama sereno

**Findings**

- [P2, resolved] Month captions used an implementation value instead of Spanish display text.
  Location: monthly card-installment rows on Inicio.
  Evidence: the source captions say “octubre 2026”; the initial implementation capture showed `2026-10` in the month kicker and installment captions.
  Impact: raw API-style period text looks unfinished in Spanish user-facing copy.
  Fix: keep the ISO period for the API and native month input, and render the visible captions with the Spanish month name. The first post-fix CUA capture shows Spanish month captions.

- [P2, resolved] The operation dialog repeated its title and broke the paid-state control into an oversized checkbox.
  Location: Ingresos, gastos y transferencias dialog.
  Evidence: initial CUA screenshot `work/product-design/task2-qa/cua-operation-dialog-390.png` shows “Registrar ingreso o gasto” in both the dialog header and an inner bordered panel. The checkbox rendered as an oversized blue square with its label squeezed into two lines. Revised screenshot `work/product-design/task2-qa/cua-dialog-v3-360.png` shows one dialog heading, no inner frame, a compact green checkbox and a readable one-line label; focus is visible on the Gasto selector.
  Impact: the duplicated framing adds visual noise, and the checkbox alignment makes the paid-state label harder to scan.
  Fix: removed the inner title and panel frame; used an 18 px brand-green checkbox with a readable 44 px label row and the sentence “Ya está cobrado o pagado”. The revised 360 px CUA screenshot confirms the visual treatment. Controller interaction and focus-return evidence is documented below.

**Open Questions**

- The native month and date controls show browser/OS locale formatting. The month input itself remains native by instruction; its browser-rendered language is not treated as an application-copy defect.
- The source is a mobile dashboard image, so there is no matching desktop source capture for a pixel comparison. Desktop layout is recorded as an implementation check only.

**Five fidelity surfaces**

- Fonts and typography: both use a clean, rounded sans-serif hierarchy. The implementation uses DM Sans; the selected raster has no supplied font metadata. At 390 px, balance values, section labels, row names and amounts remain legible. Mobile form inputs retain the required 16 px size; no typography reduction was made to compress the dashboard.
- Spacing and layout rhythm: the final same-input CUA comparison is `work/product-design/task2-qa/comparison-cua-v3-375x812.png`; focused regions are `work/product-design/task2-qa/comparison-balance-debt-v3.png` and `work/product-design/task2-qa/comparison-accounts-v3.png`. At both 360 and 390 CSS px the first account row clears the fixed nav without overlap; no horizontal overflow occurs. Two remaining account rows continue below the fold and remain available on scroll. The revised operation-dialog rendering is at `work/product-design/task2-qa/cua-dialog-v3-360.png`.
- Colors and tokens: the app’s warm-white background, dark green ink, muted labels and green primary action are close to the selected reference. Borders and pale green row icons are consistent with its quiet palette. No contrast or semantic-state mismatch was observed in the supplied dashboard capture.
- Image quality and asset fidelity: the selected dashboard contains the existing wordmark and thin line icons. The app keeps its real wordmark and uses the pinned OpenNG icon library; there are no substituted illustrations, CSS drawings or placeholder image assets.
- Copy and content: the real API fixture displays Spanish finance labels and the selected source’s illustrative account names/amounts. The period display was corrected to Spanish while the API boundary remains `yyyy-MM`. The paid-state label now reads “Ya está cobrado o pagado”. The native month/date input may use browser locale formatting.

**Comparison history**

1. Initial pass: source `work/product-design/selected-panorama.png` (853×1844 px, approximately 2.187×) and real API implementation `work/task2-finance-mobile-390.png` (390×1204 px full-page capture) were normalized to the 390×844 CSS viewport at device scale factor 1. The source was downsampled with Lanczos; the implementation was cropped at the viewport. Combined full-view evidence: `work/product-design/task2-qa/comparison-initial-390x844.png`. Focus evidence: `work/product-design/task2-qa/debt-row-initial.png` and `work/product-design/task2-qa/account-list-initial.png`. This pass found the two P2 issues above.
2. First fix and CUA comparison: removed the duplicate month kicker and duplicate home shortcuts to Cards and Compartidos (both remain accessible in existing navigation). Added Spanish captions while retaining ISO values for the period control and API. CUA capture `work/product-design/task2-qa/cua-home-390.png` is JPEG 375×812 at density 1. The browser viewport override was 390×844 with `innerWidth=390`; the rendered page content width was 375 px (`clientWidth`) due to the vertical scrollbar. The source was normalized from 853×1844 to the matching 375×812 content viewport. Combined evidence: `work/product-design/task2-qa/comparison-cua-postfix-375x812.png`. The capture still shows accounts below navigation, so the P2 remained open.
3. Second fix pass: omitted the zero-only credit row, moved per-card detail after the account rows, removed duplicate operation/transfer dialog titles and inner panel styling, and restyled the paid checkbox and label. The capped Angular production build passed with 749.73 kB initial bundle / 157.40 kB estimated transfer.
4. Controller’s old-build interaction pass: completed the onboarding wizard through UI in the owned QA database and confirmed existing account/card fixture totals remained ARS 76,900 cash, ARS 80,000 card debt and ARS 40,000 October installments. Opened the real movement dialog through the home CTA; focus entered the Gasto selector. A typed draft remained after Escape closed the dialog and restored focus; reopening showed the draft, and Cerrar closed with focus restored. No financial write was submitted. The controller has closed this session before reloading the new build.
5. Third density pass: moved currency codes beside the amounts with accessible article names, removed the redundant “DINERO REGISTRADO EN BANCOS” eyebrow, and moved monthly-detail disclosure below accounts. The capped build passed at 749.98 kB raw / 157.32 kB estimated transfer.
6. Final home comparisons: `cua-home-v3-390.png` (JPEG 375×812 px; CSS viewport 390×844, client width 375) and `cua-home-v3-360.png` (JPEG 345×809 px; CSS viewport 360×844, client width 345), both device scale 1. Source was normalized to the matching 375×812 panel for the combined 390-width comparison. The implementation never scrolls horizontally; the first account row clears the nav at both sizes. The 390 CSS DOM measurements place its bottom at 774.016 px and the nav top at 781 px; at 360 CSS px the same row/nav bounds match. Desktop CUA capture `cua-home-v3-desktop.png` is JPEG 1265×889 px at CSS viewport 1280×900 (client width 1265); dashboard columns are 559 px each, mobile nav is hidden, and no overflow occurs. There is no desktop source image to compare.
7. Final CUA interaction pass: navigated through Cards, opened and closed Cargar compra without submitting, navigated Compartidos and Más → Espacios, and confirmed the empty-household creation guard stays disabled and the Más nav state stays active. The first pass on the earlier build verified the home CTA opens the real movement dialog, focus enters Gasto, Escape closes and restores focus, reopening preserves the typed draft, and Cerrar restores opener focus without a financial write. On the revised 360 px dialog, focus remains visible on the Gasto selector, the checkbox is 18×18 px and its label row is 44 px; all fields remain readable. The console had no errors after the checked paths. The onboarding wizard was completed only in the owned QA database; its three accounts and ARS 76,900 / ARS 80,000 debt / ARS 40,000 quota fixture remained intact.
   The controller’s durable interaction log, observed DOM measurements and actual screenshot dimensions are recorded in `.superpowers/sdd/2026-10-25-product-design-refresh/task-2-cua-proof.md`; it confirms the viewport override was reset and the 5082 preview was untouched.
8. Automated supporting evidence: frontend unit tests passed 54/54; the real-API Playwright suite passed 6/6 using the dedicated disposable `enigmabudget_task2_e2e` database. It covers setup draft/return, Reader access and write denial, archived-workspace restrictions, responsive layout and real amounts. That disposable database was dropped after testing. The final capped Angular build passed at 749.98 kB initial / 157.32 kB estimated transfer; finance shell lazy chunk is 251.67 kB / 35.72 kB.

**Implementation checklist**

- [x] Inspect the selected source and combine it with a same-size normalized real API capture.
- [x] Inspect typography, spacing/layout, colors/tokens, imagery/icon fidelity and Spanish copy.
- [x] Fix the raw month captions and remove redundant home content without reducing control sizes.
- [x] Build the revised Angular production bundle.
- [x] Compare the final rendered capture with the source in one full-view and focused regions.
- [x] Controller: verify home CTA navigation, actual Optimus dialog, focus, Escape, focus return, draft preservation after reopen, and close without submitting.
- [x] Controller: verify the revised dialog layout, CUA navigation paths, responsive widths, no-overflow states and console.
- [x] Verify setup return, Reader/archived restrictions and data isolation with the real-API Playwright regression.
- [x] Reassess P0/P1/P2 findings: none remain actionable.

**Follow-up polish**

- [P3] The reference shows three account rows before navigation; the app now shows its first complete row. DOM evidence at 390×844 confirmed the row ends at 774 px before navigation at 781 px, with no horizontal overflow. Two accounts remain below the fold and are reachable by scrolling. The app keeps real workspace/month controls, readable financial text and fixed navigation instead of shrinking text or hiding controls.
- [P3] The source has slightly more open spacing and a somewhat brighter green action. Current tokens remain close; no change is needed unless future evidence shows a clear usability impact.
- [P3] Native date controls can show locale-specific text such as `October 2026` or `10/25/2026`, depending on the browser. This is an expected platform difference under the current design constraint.

Final result: passed
