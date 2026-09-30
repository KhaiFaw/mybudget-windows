# Verification record

Version 1.1.0 planning, bill-payment, simplified UI, statement-import and optional-summary build verified on Windows x64 on 30 September 2026 with .NET SDK 10.0.302. The earlier public-release record below dates from 5 August 2026. Public CI omits the optional private-PDF fixture; no personal statement is uploaded with the source or release.

## Automated checks

| Check | Result |
|---|---:|
| Core calculation, calendar and summary-privacy tests | 100 passed |
| SQLite, schema, backup, CSV, planning, PDF and Notion-client checks | 85 passed locally |
| Total | 185 passed, 0 failed; without the optional private PDF: 184 passed, 1 skipped |
| Release x64 application build | 0 warnings, 0 errors |
| Dependency advisory check, infrastructure plus transitives | No known vulnerable packages reported by the configured NuGet source |

Regression tests specifically cover refunds, savings-vs-spending, transfer exclusion, transaction/category compatibility, carry-forward and historical corrections, invalid months, PC-local date selection, leap days, recurring-income payday clamping and concurrent idempotent synchronization, schedule edits and deactivation, occurrence-only deletion and future-deposit preservation, next recurring-bill occurrences, calendar-day countdowns, bill and transaction upserts, goal-linked savings and relinking, investment contributions and as-of-month valuations, transaction destination validation, schema upgrades, non-destructive demo data, enhanced CSV destination round trips, CSV ID collisions, note fidelity, atomic export behavior, settings persistence, and backup round trips.

## Visual smoke test

The simplified interface was inspected in the running native app against a new isolated test database. Today was checked in light and dark mode, with the gradient, creator mark, icon and five-area navigation intact. The Activity list, automatic opening of the edit form, Bills checklist, Goals, Budget and nested Future plan were inspected. Marking one synthetic bill paid changed the remaining count and next-unpaid bill without adding an expense. Theme preference survived a restart. The Notion monthly-summary preview rendered with sending unavailable while unconfigured; no live Notion requests or credential setup were performed.

The final self-contained publish was launched from its delivery folder against the isolated database. The compact two-column transaction form and restored bill state rendered correctly. A missing sidebar-logo asset found in the published output was fixed by explicitly including it in publish; the corrected logo was then visually verified. The final executable remains intentionally unsigned.

A user-supplied local savings-account PDF was used only as an opt-in private integration fixture. All 15 pages and 147 transaction rows reconciled against the running balances and final totals. The native review showed the correct row count, cash directions, categories and the savings-goal selector. Cancelling after an in-memory classification change left the isolated database at zero transactions. Neither the statement, its text, account details nor real-data screenshots are stored in the repository. The system file picker displayed but the Windows inspection helper could not target its separate host; the complete read/review/cancel UI check used the supported pasted-path option instead.

Automated statement tests include cross-page descriptions, stable identifiers, different accounts, missing signs, inconsistent balances/totals, invalid dates, incomplete/repeated pages, atomic invalid-batch rejection, cancellation, goal-linked savings, unchanged bill/opening state, repeat imports after edits and concurrent imports. Notion tests use fake HTTP responses for destination validation, exact approved payloads, pagination, create/update behavior, unrelated-block preservation, ambiguous-page rejection and sanitized error handling. A live Notion workspace/token and additional Maybank layouts have **not** been verified; the Windows credential-protection path is implemented but has not been exercised with a live credential.

### Earlier local feature checks (retained for history)

Bill-payment verification used a fresh isolated database and synthetic example bills. Checking off the first bill updated the remaining count and selected the other unpaid bill; checking off both displayed “Finished for September 2026” and the October countdown. Reopening the published executable retained these states on Overview and Bills. October initially showed zero paid; marking its rent paid early in September advanced that bill to November while leaving October's internet bill as the next unpaid bill. Paid checkmarks, date labels, amounts and action buttons were inspected in both light and dark mode. A horizontal-measurement issue found during this pass was fixed with a viewport-sized Bills layout. Occurrence-only undo is covered by automated repository tests.

The local update was checked against an isolated planning profile and database using the Windows computer-use helper. The Future plan screen was observed in light and dark themes, including the conditional full-month/first-month cards, original comparison, blank actuals and nullable unknowns. Switching the selector to the first-month scenario updated its allocations and payday cautions. Saving the unchanged payslip form preserved null actuals, the original baseline and a private history copy. A width issue discovered in the first visual pass was corrected so wrapped content and comparison cards fit the viewport. Personal test profiles and captures are not included in this repository or published app. The earlier eight-screen record below is retained; this update adds Future plan as the ninth area.

The updated Release executable was launched from an isolated data directory and initialized a schema-version-three database without Windows Developer Mode or access to the normal user database. Automated migration coverage upgrades existing version-two data to version three without losing a posted recurring-income entry. The application provides eight native WinUI areas:

1. Overview
2. Plan
3. Transactions
4. Bills
5. Goals
6. Investments
7. Reports
8. Settings

The earlier seven-area visual pass confirmed light and dark rendering, saved theme preference, the PC-local date banner and transaction date, recurring-bill add/edit flow, next-due countdowns, KF mark, title-bar icon, and screen-reader automation names for the main controls. The current app initialized and remained running against isolated data, but the desktop-inspection helper could not attach to its unpackaged window; this record therefore does not claim a click-through of the new income-delete dialog.

Existing portfolio screenshots and launch smoke tests use an isolated database selected with `MYBUDGET_DATA_DIRECTORY`. The version-three migration was also tested on a copy of the existing database after creating a byte-for-byte pre-version-three backup; public-release verification did not need to modify the live database.

## Data-safety review

- SQL values are parameterized.
- CSV imports cannot silently overwrite an existing transaction ID.
- Demo data refuses a month that already contains transactions or a plan.
- Schema version checks reject unsupported future databases.
- Sequential version-zero-to-one-to-two-to-three-to-four migrations preserve existing records and seed each starter investment only once.
- Bill payments persist independently for each due month, never create duplicate expenses, survive restart and backup, preserve paid amount/date snapshots after edits, and support occurrence-only undo. Concurrent marking produces one record.
- Payment-aware calendar tests cover early/on-time/late payments, paying a future month early, remaining unpaid bills, overdue retention across rollover, short months, leap years, year boundaries, schedule lifetimes and the maximum supported year.
- The version-four upgrade was tested on synthetic version-three data. Before delivery, the closed normal-user database was copied to a timestamped pre-bill-payments backup and its hash verified; real bills were not marked paid during testing.
- Recurring-income synchronization cannot create a duplicate occurrence, including concurrent refreshes.
- Deleting one posted recurring-income occurrence records its schedule and month atomically, preventing a refresh from recreating it while future months continue.
- Goal and investment foreign keys are validated, and deleting a goal clears its transaction links without deleting transaction history.
- Investment details and their valuation commit together; a forced valuation failure rolls the new investment back.
- Exports use a temporary sibling file before atomic replacement.
- The source tree contains no tracked database, backup, export, secret, or certificate.
- Planning scenarios are separate from the transaction repository; they never post estimated salary, expenses, savings or opening balances.
- Original baseline changes are rejected; valid updates archive the previous private profile before atomic replacement.
- Unknown deductions and costs round-trip as null. Actual take-home replaces the full-month envelope only when marked as a supplied full-month payslip.
- Synthetic scenario tests cover descriptive allocation percentages, fixed-commitment funding, calendar food/buffer treatment, subscription effectiveness and confirmation, payslip reconciliation, positive-only surplus allocation, cent rounding, and pre-payday cautions.
- Normal-user database data was not used or changed during the isolated UI checks. Its SHA-256 remained unchanged. The separately supplied PDF was read for the parser/review test but no rows were committed. Personal planning figures remain outside Git and published folders.
