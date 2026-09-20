# DevPurge 🚀
> **Ultra-fast developer disk reclaimer for disposable build caches and dependency folders.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0%20%7C%208.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%2F%2010-0078D6?logo=windows&logoColor=white)](https://github.com/Behrad87/DevPurge)
[![Ko-fi](https://img.shields.io/badge/Support-Ko--fi-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/behrad87)
[![GitHub Sponsors](https://img.shields.io/badge/Sponsor-GitHub%20Sponsors-EA4AAA?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/Behrad87)

Developers frequently run out of SSD space due to dozens of forgotten repositories hoarding tens of gigabytes in build caches. General disk cleaners (WinDirStat, TreeSize) scan whole drives slowly and cannot distinguish between precious source code and disposable build artifacts.

**DevPurge** is built specifically for developers. It parallel-scans your workspaces to instantly locate, quantify, and batch-purge stale `node_modules`, `bin`/`obj`, Rust `target`, `.gradle`, and Python `.venv` directories in seconds.

---

## ✨ Features

- ⚡ **Ultra-Fast Parallel Scanner**: Uses fast directory traversal optimized to prune trees upon finding artifact boundaries, completing full workspace scans in seconds.
- 🛡️ **Safety Rails**:
  - **Never** touches `.git` repositories or parent directories.
  - **Protected System Blacklist**: Strict exclusions for `C:\Windows`, `Program Files`, and OS directories.
  - **Manifest Heuristic**: Aborts deletion if a folder contains project files (`package.json`, `Cargo.toml`, `.sln`).
- 🗑️ **Recycle Bin or Permanent Deletion**: Safe deletion moves folders to the Windows Recycle Bin for easy undo.
- ⏳ **Age Filtering**: Target only abandoned projects (e.g. untouched for `> 7 days`, `> 14 days`, or `> 30 days`).
- 🎨 **Modern Windows 11 Fluent UI**: Clean dark theme, responsive grid, real-time reclaimable counters, and one-click explorer links.
- 💻 **Headless CLI**: Automate cleanups or inspect folders straight from your terminal.
- 🔒 **Privacy First**: 100% offline, zero telemetry, zero tracking.

---

## 📦 Supported Ecosystems & Targets

| Ecosystem | Target Directories |
| :--- | :--- |
| **JavaScript / TypeScript** | `node_modules`, `.next`, `.nuxt`, `.turbo`, `.cache` |
| **.NET / C#** | `bin`, `obj`, `TestResults` |
| **Rust / Cargo** | `target` |
| **Java / Android / Gradle** | `build`, `.gradle` |
| **Python** | `.venv`, `venv`, `__pycache__`, `.pytest_cache`, `.mypy_cache` |
| **PHP / Go** | `vendor` |
| **Visual Studio** | `.vs` |

---

## 🖥️ Graphical Interface (WPF)

Launch `DevPurge.App`:
1. Pick your dev workspace folder (e.g. `D:\repos` or `C:\Users\<Name>\source`).
2. Click **Scan Workspace**.
3. Inspect discovered folders, filter by age, and select folders to reclaim.
4. Click **Purge Selected** to free gigabytes immediately.

---

## ⌨️ Command Line Interface (CLI)

DevPurge includes a standalone CLI tool for power users:

```powershell
# Safe Dry-Run (preview how much space can be reclaimed)
DevPurge.Cli --path D:\repos

# Purge folders untouched for more than 14 days
DevPurge.Cli --path D:\repos --clean --min-age 14

# Permanent deletion (bypasses Windows Recycle Bin)
DevPurge.Cli --path D:\repos --clean --permanent

# Silent execution (suitable for scripts)
DevPurge.Cli --path D:\repos --clean --min-age 30 --silent
```

---

## 🏗️ Solution Architecture

```
DevPurge\
├── src\
│   ├── DevPurge.Core\           # Reusable engine (Models, FastScanner, SafetyValidator, PurgeService)
│   ├── DevPurge.App\            # Modern WPF Desktop UI (WPF-UI Fluent Dark Theme, MVVM)
│   └── DevPurge.Cli\            # Fast headless console runner
└── tests\
    └── DevPurge.Core.Tests\     # xUnit test suite (Safety validation, age calculation, byte formatting)
```

---

## 🛠️ Building from Source

Prerequisites: [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or 10.0).

```powershell
# Clone the repository
git clone https://github.com/Behrad87/DevPurge.git
cd DevPurge

# Restore & Build
dotnet build -c Release

# Run tests
dotnet test tests/DevPurge.Core.Tests

# Run Desktop App
dotnet run --project src/DevPurge.App
```

---

## 💖 Support & Donations

DevPurge is 100% free and open source. If this tool saved your SSD space or sped up your workflow, please consider buying me a coffee or starring the repository:

- ⭐ **Star this repository** on GitHub
- ☕ **Support on Ko-fi:** [ko-fi.com/behrad87](https://ko-fi.com/behrad87)
- 💖 **Sponsor on GitHub:** [github.com/sponsors/Behrad87](https://github.com/sponsors/Behrad87)

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.
