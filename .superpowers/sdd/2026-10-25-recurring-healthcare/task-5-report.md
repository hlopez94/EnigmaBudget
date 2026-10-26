# Task 5 report: recurring estimated projection adapter

## Implementation

- Added `RecurringProjectionAdapter`, which reads recurring estimates through `RecurringCalendar.GetPageAsync`, follows its cursors across the bounded 36-month windows, loads only the page revisions’ descriptions and preferred accounts, and maps unhandled occurrence IDs and scheduled dates into projection rows.
- Kept unhandled estimates out of Confirmed GET. Estimated calculations include them before cash aggregation and assumption matching. Past row dates remain unchanged; daily cash uses the existing today mapping. Scenario overrides match stable occurrence identity and rebuild cash/free totals for both assigned and unassigned rows.
- Wrapped projection GET and Calculate reads in an owned PostgreSQL RepeatableRead, READ ONLY transaction when no transaction exists. Existing caller transactions are reused. This covers account, ledger, finance operation, card, savings balance, and recurring calendar reads.
- Added a database command interceptor checkpoint and a real API record command test to prove a concurrent record produces either the previous estimate or the confirmed operation, without both or neither.
- Added an explicit warning when the calendar reports older omitted history. Projection reads and scenario calculations do not write operation, action, settlement, or rule rows.

## Test evidence

The test connection was loaded privately from `work/workspace-test-connection.txt`. Test processes ran sequentially with available memory above the 6 GiB floor, in a user scope capped at 4 GiB RAM and 512 MiB swap, with one MSBuild worker and shared compilation disabled.

- RED command: `work/recurring/task5/red-command.txt`
- RED stdout: `work/recurring/task5/red-stdout.txt` — 5 failed, 15 passed, 20 total. Failures showed no projected dates, no older-history warning, no stable-source scenario target, and the concurrent record case returning neither estimate nor actual. The five test cases are `estimated_projection_keeps_each_overdue_occurrence_separate`, `recorded_date_is_suppressed_without_suppressing_later_months`, `projection_older_history_cap_emits_warning`, `scenario_can_adjust_recurring_occurrence_by_stable_source_identity`, and `concurrent_occurrence_record_during_projection_never_counts_estimate_and_actual_together`.
- GREEN command: `work/recurring/task5/green-command.txt`
- GREEN stdout: `work/recurring/task5/green-stdout.txt` — 24 passed, 0 failed. This includes the 200-item cursor and multiple-window test, independent ARS/USD cash, personal workspace isolation, and the scenario no-write assertion.
- Adjacent regression command: `work/recurring/task5/adjacent-command.txt`
- Adjacent stdout: `work/recurring/task5/adjacent-stdout.txt` — 39 passed, 0 failed across ProjectionIntegrationTests, RecurringOccurrenceIntegrationTests, and OperationSettlementIntegrationTests.
- `git diff --check` passed.

The controller independently reported reviewed base CI run `40258407054` / job `121365172692` as successful before this Task 5 change. Its broad suite was not repeated here.

The controller also observed an earlier focused RED before the checkpoint test was added: 4 failed, 15 passed, 19 total. That earlier stdout was overwritten by the later saved 20-test RED run; it is recorded here as controller-observed evidence, not as a retained log. `ProjectionContracts.cs` did not need changes because its existing `ProjectionRowDto` and `ProjectionAlertDto` fields already express the required data.

## Self-review

- Estimate rows keep one stable occurrence ID per scheduled date, original overdue dates, currency, direction, decimal-string amount, Spanish description, and warning. Attended dates are suppressed by the calendar; later dates stay visible.
- Cash and free balances include recurring rows once at the assigned account or currency level. Existing operation settlement and canonical card projection code remains the source for recorded operations and linked card installments.
- Workspace filters are applied to calendar revisions and preferred accounts. No frontend, preview, user database, or private credential changes were made.
- No remaining concerns identified.

## Commit

Pending.
