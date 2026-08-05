# Verification record

Verified on Windows x64 on 5 August 2026 with .NET SDK 10.0.302.

## Automated checks

| Check | Result |
|---|---:|
| Core calculation and calendar tests | 65 passed |
| SQLite, schema, backup, and CSV tests | 40 passed |
| Total | 105 passed, 0 failed |
| Release x64 application build | 0 warnings, 0 errors |

Regression tests specifically cover refunds, savings-vs-spending, transfer exclusion, transaction/category compatibility, carry-forward and historical corrections, invalid months, PC-local date selection, leap days, recurring-income payday clamping and concurrent idempotent synchronization, schedule edits and deactivation, occurrence-only deletion and future-deposit preservation, next recurring-bill occurrences, calendar-day countdowns, bill and transaction upserts, goal-linked savings and relinking, investment contributions and as-of-month valuations, transaction destination validation, schema upgrades, non-destructive demo data, enhanced CSV destination round trips, CSV ID collisions, note fidelity, atomic export behavior, settings persistence, and backup round trips.

## Visual smoke test

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
- Sequential version-zero-to-one-to-two-to-three migrations preserve existing records and seed each starter investment only once.
- Recurring-income synchronization cannot create a duplicate occurrence, including concurrent refreshes.
- Deleting one posted recurring-income occurrence records its schedule and month atomically, preventing a refresh from recreating it while future months continue.
- Goal and investment foreign keys are validated, and deleting a goal clears its transaction links without deleting transaction history.
- Investment details and their valuation commit together; a forced valuation failure rolls the new investment back.
- Exports use a temporary sibling file before atomic replacement.
- The source tree contains no tracked database, backup, export, secret, or certificate.
