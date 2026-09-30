# Privacy and data safety

## What stays local

MyBudget does not require an account and does not include telemetry, advertising, analytics, automatic cloud synchronization, or bank-login access. Budget entries are stored in a SQLite file under the signed-in Windows user's local application-data folder. The optional Notion connection sends only a manually approved monthly summary; see below.

Optional future-planning figures, salary estimates, supplied payslip figures and original comparisons are stored beside the database in `planning-profile.json`. Prior versions are retained in `planning-history`. Those files are private local data, not app assets, source examples or release contents. Importing a profile copies it into this local storage; it does not post money entries.

## What is not encrypted by the app

The SQLite database, CSV exports, and database backups are not encrypted by MyBudget. Anyone who can read those files may be able to read the financial data. Use a protected Windows account, enable device encryption where available, lock the PC when away, and share exports or backups carefully.

Planning profiles and their history are also unencrypted. Database backup and CSV export do not include them: back up their files separately if needed. Public screenshots must use synthetic profiles, never a person's salary or real cost list.

## Safer data entry

### Maybank PDF import

Statement PDFs are parsed locally with PdfPig, without AI, uploads or bank credentials. The supported layout is a Maybank savings/current-account statement with signed transaction amounts, running balances and final debit/credit totals. The complete statement must reconcile before the review screen appears. Scanned documents, credit-card statements, unsupported layouts and unverifiable totals are rejected instead of guessed.

Selecting a PDF does not create transactions. Choose and categorize rows in the review screen, then confirm the import. Own-account transfers, credit-card repayments, refunds and salary already recorded by a schedule need particular attention. Potential matching entries are flagged, not silently merged. Stable statement IDs prevent repeat imports from overwriting or duplicating existing imported rows, including edited ones. Deleting an imported entry allows it to be imported again later.

The original PDF is not copied into app storage; its opening balance, closing balance, raw account number and PDF password are not saved. Only selected transaction dates, amounts, descriptions, classifications, optional goal links and hashed identifiers enter the database. Descriptions can still identify merchants or payees: treat the database and exports as private. Statement validation is not a malware scan; only open PDFs from a source you trust. Bank restrictions requiring an opening password are honored, with the supplied password held only for that read.

### Optional Notion summaries

Notion is off until configured in Settings. The user creates an internal Notion connection, grants it access to a dedicated parent page and enters its token **inside the app**, not in source or chat. The token is protected with Windows user-scoped data protection and saved separately in `notion-connection.json` beside the database. Other processes running as that Windows user may still be able to access it; this is not protection from a compromised account. It is excluded from database backups and releases.

Every send has an explicit preview showing the destination and complete summary. Only the selected month, currency, carry-forward, recorded income/spending/savings, available budget, allocated budget and bill-completion count are sent over HTTPS to `api.notion.com`. Notion and users with access to that destination can read those totals. Purchase descriptions, account identifiers, PDFs, goals, investment holdings, private future-plan figures and payslip details are not in the payload. There is no background sync and no import from Notion.

Sending again updates the matching month's MyBudget summary paragraph; unrelated page content is left alone. An ambiguous existing page stops the send. Forgetting a connection removes its local credential, not any previously shared pages. Revoke the connection in Notion to invalidate its token; review Notion page-sharing permissions yourself.

### Everyday precautions

- Avoid putting bank account numbers, passwords, PINs, or government identifiers in notes.
- Store backups on a trusted encrypted device or protected folder.
- Delete exports after their intended use.
- Use the included demo-data option for screenshots and portfolio material.

## Git safety

The repository ignores database files, exports, backups, certificates, and build output. Before every commit, review `git status` and the staged diff. Synthetic data and screenshots should never contain personal financial details.

The default planning-profile filename, history directory, statement folder and Notion credential filename are also ignored as an extra safeguard. Do not copy personal PDFs, profiles or credentials under another name into the repository or app publish folder. Private integration fixtures are supplied through an environment variable and are never committed.
