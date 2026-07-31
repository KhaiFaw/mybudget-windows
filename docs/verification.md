# Verification record

Verified on Windows x64 on 1 August 2026 with .NET SDK 10.0.302.

## Automated checks

| Check | Result |
|---|---:|
| Core calculation and calendar tests | 27 passed |
| SQLite, schema, backup, and CSV tests | 18 passed |
| Total | 45 passed, 0 failed |
| Release x64 application build | 0 warnings, 0 errors |
| NuGet vulnerability audit | No known vulnerable packages |

Regression tests specifically cover refunds, savings-vs-spending, transfer exclusion, transaction/category compatibility, invalid months, 29th–31st recurring dates, schema versions, non-destructive demo data, CSV ID collisions, note fidelity, atomic export behavior, settings persistence, and backup round trips.

## Visual smoke test

The self-contained Release executable was launched directly without Windows Developer Mode. The following native WinUI screens rendered and were navigable:

1. Overview
2. Plan
3. Transactions
4. Bills
5. Goals
6. Reports
7. Settings

The light theme rendered correctly, the dark switch visibly changed the complete interface, and the preference was saved locally. The test also confirmed that screen-reader automation names expose the main navigation, month controls, forms, status, and theme controls.

## Data-safety review

- SQL values are parameterized.
- CSV imports cannot silently overwrite an existing transaction ID.
- Demo data refuses a month that already contains transactions or a plan.
- Schema version checks reject unsupported future databases.
- Exports use a temporary sibling file before atomic replacement.
- The source tree contains no tracked database, backup, export, secret, or certificate.
