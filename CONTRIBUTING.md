# Contributing to MyBudget

Thank you for taking the time to review MyBudget.

## Feedback is welcome

Bug reports and focused feature suggestions are welcome through GitHub Issues. Before opening an issue, check whether a similar one already exists.

A useful bug report includes:

- the Windows version and MyBudget version or commit
- clear steps to reproduce the problem
- what you expected and what happened instead
- a screenshot when it is safe and helpful

Never attach a real MyBudget database, backup, CSV export, financial screenshot, account detail, or other sensitive information. A report should use synthetic example data.

Security-sensitive problems should not be filed as public issues; follow [SECURITY.md](SECURITY.md) instead.

## Source contributions

This is a source-visible proprietary portfolio project, not an open-source project. It does not currently accept unsolicited pull requests or copied implementations.

If the maintainer explicitly invites a contribution, agree on its scope and written terms before submitting code. Submission does not override the copyright and source-use terms in [COPYRIGHT.md](COPYRIGHT.md).

## Project expectations

When discussing a proposed change, keep these product boundaries in mind:

- budgeting data remains local by default
- money calculations use exact decimal values
- historical edits must keep carry-forward accurate
- recurring activity must be duplicate-safe
- changes must preserve existing user data through tested migrations
- the app must remain understandable in both light and dark mode
- no real personal financial data belongs in tests, screenshots, issues, or commits

Thanks for helping make the project clearer and more dependable.
