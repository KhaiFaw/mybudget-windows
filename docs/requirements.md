# Product requirements

## Goal

Make monthly budgeting fast enough to become a habit while keeping the user's data on their Windows PC.

## First-release scope

1. Create or open a month and navigate to adjacent months.
2. Record income, expenses, savings, refunds, and transfers.
3. Set a planned amount for each expense or savings category.
4. Show income, planned, spent, saved, and available as distinct totals.
5. Warn when a category exceeds its plan without blocking entry.
6. Track recurring bills, including due days that do not exist in shorter months.
7. Track savings goals independently of monthly spending.
8. Show category progress and monthly reports.
9. Persist the chosen light or dark theme.
10. Export transactions to CSV, import compatible CSV, and create a database backup.
11. Offer synthetic demo data without mixing it with real data unexpectedly.

## Calculation rules

- `available = income - expenses - savings`
- refunds reduce expenses
- transfers move money but do not count as income or spending
- planned money is compared with income separately from actual spending
- currency values use `decimal`, never binary floating-point
- monthly date ranges are inclusive at the start and exclusive at the next month
- recurring due days from 29 to 31 clamp to the last valid day of a month

## Important boundaries

- No bank credential storage or bank scraping
- No online account, cloud sync, telemetry, advertisements, or automatic data upload
- No financial or investment advice
- No claim that the unencrypted database protects data from someone with access to the Windows account

## Acceptance checks

- All automated tests pass from a clean restore.
- The app builds for x64 on Windows.
- Light and dark themes retain readable contrast.
- No real database, export, backup, secret, or signing certificate is tracked by Git.
- Closing and reopening the app retains data and the theme preference.
