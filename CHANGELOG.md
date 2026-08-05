# Changelog

Notable public releases of MyBudget are recorded here.

## 1.0.2 — 5 August 2026

### Changed

- Granted personal and other non-commercial use under the PolyForm Strict License 1.0.0
- Clarified that modification, redistribution, republication, selling, and monetization require prior written permission
- Added the license, permission summary, and creator terms to the repository, app Settings screen, and downloadable package

## 1.0.1 — 5 August 2026

### Changed

- Added “Personal Finance and Budget Analytics Application” as MyBudget's professional subtitle in the app title bar and public portfolio presentation
- Added the same descriptor to Windows package and executable metadata while keeping the product name, repository, and executable identity unchanged

## 1.0.0 — 5 August 2026

First public portfolio release of the local-first Windows budget planner.

### Included

- Eight native WinUI screens covering overview, planning, transactions, bills, goals, investments, reports, and settings
- PC-local daily entries, recurring income and bills, automatic carry-forward, and editable historical transactions
- Goal-linked savings and investment contributions for Tabung Haji, ASB, Maybank Gold, and custom holdings
- Light and dark themes, selectable display currency, CSV portability, local backup, and synthetic demo data
- Custom Windows icon and KF creator mark

### Reliability and privacy

- 105 automated tests for budget rules, dates, migrations, SQLite persistence, CSV, backups, and settings
- Sequential data-preserving database migrations through schema version 3
- Local-only storage with no account, advertising, analytics, telemetry, bank connection, or cloud synchronization
- Self-contained unsigned Windows x64 release; see the README before downloading or running it
