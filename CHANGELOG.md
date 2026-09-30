# Changelog

Notable public releases of MyBudget are recorded here.

## 1.1.0 — 30 September 2026

### Added

- A quieter Today screen, five primary navigation areas, expandable entry forms, and advanced planning/insights nested under Budget
- Local Maybank savings/current-account PDF import with signed-amount parsing, multi-page descriptions, complete balance/total validation, classification review, possible-duplicate warnings, optional savings-goal links and atomic duplicate-safe saves
- Optional manual Notion monthly summaries, preview-before-send, user-encrypted local credentials and safe repeat-send updates without touching unrelated page content
- 31 regression checks for summary privacy, Notion requests/errors, statement validation and reviewed imports; one optional private-PDF check runs only when explicitly configured

### Changed

- Retired the manual Investments screen and new investment contribution choices; existing records, valuations and transaction links remain in the database and backups
- Startup always opens Today; the private Future plan remains available under Budget
- No bank-account connection or AI processing was added; statement import does not assume an opening balance or automatically check bills off

### Planning and bill-payment improvements

- A private Future plan screen with preserved original figures and current, proposed and confirmed subscription scenarios
- Separate provisional full-month and first-month income envelopes; estimates never create transactions
- Full funding of recorded commitments, calendar-day food budgeting, buffer-first adjustments and first-payday cautions
- Needs/wants/savings percentages that describe allocations rather than impose targets; buffer remains distinct
- Actual payslip inputs with blank unknown deductions and a separate additional-recurring-income allocation rule
- Nullable unknown costs and balances, validated local profile imports, atomic saves and private version history
- 27 additional synthetic regression tests; personal planning data is excluded from source and portable builds
- Monthly bill checklists with paid checkmarks, paid-date labels, undo, and a finished-month summary
- Payment-aware upcoming countdowns that skip paid months and retain overdue bills across month changes
- Schema-four payment history keyed by bill and due month, with saved amount/due-date snapshots and no automatic expense duplication
- 22 additional bill-payment regression cases for early payments, rollover, edits, persistence, concurrency, backups and migration

### Release status

- Development is paused after this release; the repository and official Windows download remain available
- Existing local budgets and private planning profiles are retained; neither personal data nor Notion credentials are bundled
- Notion requires user setup and has not been verified against a live workspace; scans, credit-card PDFs and untested statement layouts are not supported

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
