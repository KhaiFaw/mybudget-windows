# MyBudget for Windows

MyBudget is a modern, local-first monthly budget planner built for Windows. It helps one person plan income, spending, savings, recurring bills, and goals without creating an account or sending financial data to a cloud service.

> Source availability: this repository is private and proprietary. A separate public showcase is intended for portfolio viewing. See [COPYRIGHT.md](COPYRIGHT.md).

## Product preview

| Light | Dark |
|---|---|
| ![MyBudget light dashboard](docs/screenshots/mybudget-dashboard-light.png) | ![MyBudget dark dashboard](docs/screenshots/mybudget-dashboard-dark.png) |

The screenshots are approved visual targets. The running application follows the same information hierarchy while using native WinUI controls and responsive layouts.

## Features

- Monthly dashboard with income, planned spending, actual spending, savings, and available money kept separate
- Category plans with clear over-budget warnings
- Income, expense, savings, refund, and transfer transaction types
- Recurring bills with safe end-of-month due-date handling
- Savings goals and progress tracking
- Monthly category reports
- Persistent light and dark Windows styling
- Local SQLite storage, explicit backup, and CSV import/export
- Optional synthetic demo data; no real financial details are committed
- No sign-in, advertising, analytics, or cloud synchronization

## Technology

- C# 14 and .NET 10 LTS
- WinUI 3 and Windows App SDK
- MVVM with CommunityToolkit.Mvvm
- SQLite through Microsoft.Data.Sqlite
- MSTest for domain and persistence tests

The solution separates the UI, money rules, and persistence:

```text
MyBudget.App  ->  MyBudget.Core
      |                   ^
      +-> MyBudget.Infrastructure
```

See [docs/architecture.md](docs/architecture.md) for the design decisions.

## Build and test

Prerequisites: Windows 10 version 1809 or later and the .NET 10 SDK. The app is built as a self-contained, unpackaged WinUI application, so Windows Developer Mode is not required.

```powershell
dotnet restore src/MyBudget.App/MyBudget.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests/MyBudget.Core.Tests/MyBudget.Core.Tests.csproj -c Release
dotnet test tests/MyBudget.Infrastructure.Tests/MyBudget.Infrastructure.Tests.csproj -c Release
dotnet build src/MyBudget.App/MyBudget.App.csproj -c Release --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet run --project src/MyBudget.App/MyBudget.App.csproj -c Release --no-build -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet publish src/MyBudget.App/MyBudget.App.csproj -c Release --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --self-contained true -o artifacts/MyBudget-win-x64
```

The publish command creates a portable folder with `artifacts/MyBudget-win-x64/MyBudget.App.exe`. Keep that folder together when moving it to another PC. The application database is created under the current Windows user's local application-data folder. Database files, exports, backups, signing certificates, and build output are excluded from Git.

## Privacy and limitations

The current app is local-first, but the SQLite database is not encrypted at rest. Windows account protection and device encryption remain important. Review [docs/privacy.md](docs/privacy.md) before entering sensitive notes or sharing a backup.

## Status

This is an actively developed personal portfolio project. The current Release build passes 45 automated tests and a seven-screen light/dark visual smoke test. See [docs/verification.md](docs/verification.md) for reproducible evidence.
