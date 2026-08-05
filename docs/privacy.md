# Privacy and data safety

## What stays local

MyBudget does not require an account and does not include telemetry, advertising, analytics, or cloud synchronization. Budget entries are stored in a SQLite file under the signed-in Windows user's local application-data folder.

## What is not encrypted by the app

The SQLite database, CSV exports, and database backups are not encrypted by MyBudget. Anyone who can read those files may be able to read the financial data. Use a protected Windows account, enable device encryption where available, lock the PC when away, and share exports or backups carefully.

## Safer data entry

- Avoid putting bank account numbers, passwords, PINs, or government identifiers in notes.
- Store backups on a trusted encrypted device or protected folder.
- Delete exports after their intended use.
- Use the included demo-data option for screenshots and portfolio material.

## Git safety

The repository ignores database files, exports, backups, certificates, and build output. Before every commit, review `git status` and the staged diff. Synthetic data and screenshots should never contain personal financial details.
