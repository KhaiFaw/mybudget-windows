# Verification record

Verified on Windows x64 on 2 August 2026 with .NET SDK 10.0.302.

## Automated checks

| Check | Result |
|---|---:|
| Core calculation and calendar tests | 65 passed |
| SQLite, schema, backup, and CSV tests | 36 passed |
| Total | 101 passed, 0 failed |
| Release x64 application build | 0 warnings, 0 errors |

Regression tests specifically cover refunds, savings-vs-spending, transfer exclusion, transaction/category compatibility, carry-forward and historical corrections, invalid months, PC-local date selection, leap days, recurring-income payday clamping and concurrent idempotent synchronization, schedule edits and deactivation, next recurring-bill occurrences, calendar-day countdowns, bill and transaction upserts, goal-linked savings and relinking, investment contributions and as-of-month valuations, transaction destination validation, schema upgrades, non-destructive demo data, enhanced CSV destination round trips, CSV ID collisions, note fidelity, atomic export behavior, settings persistence, and backup round trips.

## Visual smoke test

The updated Release executable was first launched from an isolated data directory and initialized its native WinUI window without Windows Developer Mode or access to the normal user database. A copy of the existing version-one database was then upgraded successfully to schema version two before the live database was backed up and migrated. The application now provides eight native WinUI areas:

1. Overview
2. Plan
3. Transactions
4. Bills
5. Goals
6. Investments
7. Reports
8. Settings

The earlier seven-area visual pass confirmed light and dark rendering, saved theme preference, the PC-local date banner and transaction date, recurring-bill add/edit flow, next-due countdowns, KF mark, title-bar icon, and screen-reader automation names for the main controls. A fresh manual click-through and portfolio screenshot set for the expanded eight-area interface remain release checks; this record does not claim that the new controls were exercised through desktop automation.

Existing portfolio screenshots and the initial updated launch smoke test use an isolated database selected with `MYBUDGET_DATA_DIRECTORY`. The live migration was run only after creating a byte-for-byte pre-version-two backup in the normal data directory.

## Data-safety review

- SQL values are parameterized.
- CSV imports cannot silently overwrite an existing transaction ID.
- Demo data refuses a month that already contains transactions or a plan.
- Schema version checks reject unsupported future databases.
- Sequential version-zero-to-one-to-two migrations preserve existing records and seed each starter investment only once.
- Recurring-income synchronization cannot create a duplicate occurrence, including concurrent refreshes.
- Goal and investment foreign keys are validated, and deleting a goal clears its transaction links without deleting transaction history.
- Investment details and their valuation commit together; a forced valuation failure rolls the new investment back.
- Exports use a temporary sibling file before atomic replacement.
- The source tree contains no tracked database, backup, export, secret, or certificate.
