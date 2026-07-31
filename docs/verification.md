# Verification record

Verified on Windows x64 on 1 August 2026 with .NET SDK 10.0.302.

## Automated checks

| Check | Result |
|---|---:|
| Core calculation and calendar tests | 42 passed |
| SQLite, schema, backup, and CSV tests | 19 passed |
| Total | 61 passed, 0 failed |
| Release x64 application build | 0 warnings, 0 errors |
| NuGet vulnerability audit | No known vulnerable packages |

Regression tests specifically cover refunds, savings-vs-spending, transfer exclusion, transaction/category compatibility, invalid months, PC-local date selection, leap days, next recurring occurrences, calendar-day countdowns, safe monthly-income adjustment, bill and transaction upserts, schema versions, non-destructive demo data, CSV ID collisions, note fidelity, atomic export behavior, settings persistence, and backup round trips.

## Visual smoke test

The self-contained Release executable was launched directly without Windows Developer Mode. The following native WinUI screens rendered and were navigable:

1. Overview
2. Plan
3. Transactions
4. Bills
5. Goals
6. Reports
7. Settings

The light theme rendered correctly, the dark switch visibly changed the complete interface, and the preference was saved locally. A live synthetic-data walkthrough also confirmed the PC-local date banner and transaction date, one-click monthly-income update, recurring-bill add/edit flow, next-due countdowns, KF mark, and the new title-bar icon. The test also confirmed that screen-reader automation names expose the main navigation, month controls, forms, status, and theme controls.

All portfolio screenshots were captured from an isolated database selected with `MYBUDGET_DATA_DIRECTORY`; the normal user database was not read or modified.

## Data-safety review

- SQL values are parameterized.
- CSV imports cannot silently overwrite an existing transaction ID.
- Demo data refuses a month that already contains transactions or a plan.
- Schema version checks reject unsupported future databases.
- Exports use a temporary sibling file before atomic replacement.
- The source tree contains no tracked database, backup, export, secret, or certificate.
