# Security policy

## Supported version

Security fixes are considered for the latest code on the repository's default branch. Older snapshots and third-party copies are not supported.

## Reporting a vulnerability

Please do not disclose a suspected vulnerability, exploit details, or sensitive data in a public issue.

Use GitHub's **Report a vulnerability** option if it is available in the repository's Security tab. If it is unavailable, contact the maintainer through the contact method on the [KhaiFaw GitHub profile](https://github.com/KhaiFaw) and request a private reporting channel.

Include only what is needed to reproduce and understand the issue:

- the affected version or commit
- the Windows version
- reproduction steps using synthetic data
- the expected and observed behavior
- the potential impact

Do not send a real MyBudget database, backup, CSV export, financial screenshot, credential, or other personal information.

## Security scope and current limitations

MyBudget has no account system, bank connection, telemetry, or cloud synchronization. Its SQLite database, CSV exports, and backup files are local but are not encrypted by MyBudget. Anyone with sufficient access to the Windows account or an unprotected copy of those files may be able to read them.

Use Windows account protection and device encryption, and store exported or backed-up files carefully. This project does not provide financial or investment advice.

Third-party .NET and Windows dependencies remain subject to their own security policies and update schedules.
