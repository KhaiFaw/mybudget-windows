# Product requirements

## Goal

Make monthly budgeting fast enough to become a habit while keeping the user's data on their Windows PC.

## First-release scope

1. Create or open a month and navigate to adjacent months.
2. Record income, expenses, savings, refunds, and transfers.
3. Set a planned amount for each expense or savings category.
4. Show carry-forward, income, planned, spent, saved, and available as distinct totals.
5. Warn when a category exceeds its plan without blocking entry.
6. Track recurring bills, including due days that do not exist in shorter months.
7. Track savings goals from their starting amounts plus savings transactions linked to each goal.
8. Show category progress and monthly reports.
9. Persist the chosen light or dark theme.
10. Export transactions to CSV, import compatible CSV, and create a database backup.
11. Offer synthetic demo data without mixing it with real data unexpectedly.
12. Resolve today from the PC's local calendar, default new entries to that day, and refresh an open app after midnight.
13. Let the user configure a monthly income amount and payday, then create each due income entry automatically without duplicate deposits.
14. Let the user edit recurring bills and show the nearest occurrence as a calendar-day countdown.
15. Provide a recognizable multi-resolution Windows icon in the EXE, title bar, and taskbar.
16. Carry the previous closing balance into the next month automatically and recalculate it after historical edits or deletions.
17. Let the user edit an existing transaction without deleting and recreating it.
18. Categorize income with income-compatible categories such as Salary and Other income rather than displaying it as uncategorized.
19. Let a savings transaction target either a savings goal or an investment, while preventing one transaction from targeting both.
20. Track Tabung Haji, ASB, Maybank Gold, and additional user-created investments with contributions, optional dated valuations, gain/loss summaries, and restorable archives.
21. Let the user edit or delete an already-posted recurring-income entry without changing its schedule or future deposits.

## Calculation rules

- `carry-forward = all income + refunds - expenses - savings before the selected month`; transfers have no cash impact
- `available = carry-forward + income + refunds - expenses - savings`
- refunds reduce expenses
- transfers move money but do not count as income or spending
- planned money is compared with carry-forward plus income separately from actual spending
- currency values use `decimal`, never binary floating-point
- monthly date ranges are inclusive at the start and exclusive at the next month
- recurring due days from 29 to 31 clamp to the last valid day of a month
- bill countdowns use local calendar days rather than elapsed UTC hours
- recurring-income synchronization follows the PC-local date and is idempotent, so reopening or refreshing cannot post the same scheduled deposit twice
- changes to a recurring-income schedule apply from their effective month without rewriting deposits that have already been posted
- editing or deleting a posted recurring-income entry affects only that occurrence; deleting it suppresses automatic reposting for that schedule and month while later deposits continue normally
- editing a transaction preserves its identity and immediately recalculates its month, future carry-forward, linked goal, and linked investment as applicable
- only savings transactions may link to a goal or investment, and each transaction may have at most one destination
- a goal's current amount is its starting amount plus all savings transactions linked to it
- an investment's contributed amount comes from linked savings transactions; its displayed value uses the latest valuation on or before the selected month's end, or contributions when no valuation exists

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
- Opening the app selects the PC-local current month and day; leaving it open across midnight refreshes the day automatically.
- A recurring bill can be edited without creating a duplicate, and the nearest due occurrence is ordered first.
- A recurring-income schedule posts once on its local-calendar payday, including a clamped payday in a shorter month.
- A posted recurring-income entry can be edited or deleted from Transactions; deleting it does not recreate it on refresh or alter the schedule's future deposits.
- Moving between months shows a carry-forward derived from all earlier cash activity without requiring a manual opening balance.
- Editing or deleting a historical transaction updates later carry-forward totals.
- Editing a transaction retains one record and applies the new category or savings destination.
- Linking, relinking, editing, or deleting a savings transaction updates the appropriate goal or investment total without double counting.
- The Investments screen can manage the three starter investments and additional custom entries, contributions, and valuations.
