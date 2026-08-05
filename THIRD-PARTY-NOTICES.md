# Third-party software notices

MyBudget is proprietary software, but it is built with third-party components that remain under their own licenses.

The self-contained Windows release includes components from these projects:

| Component | Version | License or terms |
|---|---:|---|
| .NET runtime and libraries | 10.0 | MIT and accompanying third-party notices |
| .NET Community Toolkit MVVM | 8.4.2 | MIT and accompanying third-party notices |
| Microsoft.Data.Sqlite | 10.0.10 | MIT |
| SQLite | 3.53.4 | Public domain notice |
| SQLitePCLRaw | 3.0.5 | Apache-2.0 |
| Microsoft Windows App SDK | 1.8.260317003 | Microsoft Windows App SDK license and accompanying notices |
| Microsoft WebView2 SDK | 1.0.3179.45 | BSD-style license and accompanying notices |
| System.Numerics.Tensors | 9.0.0 | MIT and accompanying third-party notices |

Build-only Windows SDK packages are governed by their own Microsoft license terms and are not application source owned by MyBudget.

The published folder places verbatim upstream license and notice files under `licenses/`. Those files control if this summary differs from an upstream term. Package names, product names, and trademarks belong to their respective owners.

Upstream project and license information is available from the NuGet package metadata restored by the versions declared in `src/MyBudget.App/MyBudget.App.csproj` and its dependency graph.
