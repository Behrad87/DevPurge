# DevPurge v1.1.0 - Modern Fluent UI, KPI Analytics & Windows Installer 🚀

We are thrilled to announce **DevPurge v1.1.0**! This milestone release introduces an **official Windows Setup Installer**, a complete **Fluent 2.0 UI redesign** with real-time KPI metrics, category filtering, an **animated 3D TitleBar heart**, and refreshed single-file cross-platform binaries.

---

## 📦 Downloads & Binaries

| Asset | Platform | Architecture | Type | Description |
| :--- | :--- | :--- | :--- | :--- |
| **`DevPurge-v1.1.0-windows-x64-installer.exe`** | Windows 10 / 11 | `x64` | **Setup Installer (Recommended)** | Full installer with Desktop & Start Menu shortcuts, GUI + CLI, and uninstaller |
| **`DevPurge-v1.1.0-windows-x64-gui.zip`** | Windows 10 / 11 | `x64` | Portable GUI Package | Standalone folder with `DevPurge.App.exe` (no setup required) |
| **`DevPurge-v1.1.0-windows-x64-cli.zip`** | Windows 10 / 11 | `x64` | Headless Single-File CLI | Standalone terminal CLI for Windows |
| **`DevPurge-v1.1.0-linux-x64-cli.tar.gz`** | Linux (Ubuntu, Debian, Fedora, Arch) | `x64` | Headless Single-File CLI | Standalone terminal CLI for Linux x64 |
| **`DevPurge-v1.1.0-linux-arm64-cli.tar.gz`** | Linux (Raspberry Pi, ARM64 Cloud) | `arm64` | Headless Single-File CLI | Standalone terminal CLI for Linux ARM64 |

*All binaries are **100% self-contained**. Zero external .NET runtime installation required.*

---

## ✨ What's New in v1.1.0

### 💿 Official Windows Setup Installer
- Built with **Inno Setup 6** for a clean, modern Windows installation experience.
- Standard user installation (`PrivilegesRequired=lowest`): install instantly without requiring administrator permissions.
- Installs both **DevPurge GUI** and **DevPurge CLI** into your user application directory.
- Automatically creates Desktop and Start Menu shortcuts, registers standard Windows "Installed Apps" uninstall entry, and offers optional CLI `PATH` integration.

### 📊 Real-Time KPI Analytics Dashboard
- **Artifacts Discovered**: Live count of scan matches across your repos.
- **Reclaimable Space**: High-visibility gigabyte/megabyte counter of cleanable bloat.
- **Primary Bloat Ecosystem**: Identifies the framework consuming the most disk space (e.g., Node.js, .NET, Rust).
- **Average Artifact Age**: Highlights stale caches and abandoned build folders.

### 🏷️ Multi-Ecosystem Category Filter Chips
- One-click filtering for **All**, **JavaScript / Web** (`node_modules`, `.next`), **.NET / C#** (`bin`, `obj`), **Rust** (`target`), **Python** (`.venv`, `__pycache__`), **Java / Android** (`.gradle`, `build`), and **Visual Studio / IDE** (`.vs`).

### 💖 Animated 3D TitleBar Support Heart
- Embedded right inside the native Windows 11 TitleBar beside the window control buttons.
- Features a rich crimson-to-ruby red radial gradient, subtle depth drop shadow, and a smooth 3D isometric perspective tilt with continuous heartbeat animation.
- One-click access to support DevPurge development on GitHub Sponsors, Ko-fi, and Reymit.

---

## 🚀 Quickstart

### Option 1: Windows Setup Installer (Recommended)
1. Download **`DevPurge-v1.1.0-windows-x64-installer.exe`**.
2. Run the installer and follow the setup wizard.
3. Launch **DevPurge** from your Start Menu or Desktop!

### Option 2: Portable Windows GUI
1. Download and extract **`DevPurge-v1.1.0-windows-x64-gui.zip`**.
2. Run **`DevPurge.App.exe`**.
3. Select your workspace (e.g., `C:\repos` or `D:\repos`) and click **Scan Workspace**.

### Option 3: Linux CLI
```bash
# 1. Download and extract
tar -xzvf DevPurge-v1.1.0-linux-x64-cli.tar.gz

# 2. Grant execution permission
chmod +x DevPurge.Cli

# 3. Safe Dry-Run (preview disposable space)
./DevPurge.Cli --path ~/repos

# 4. Clean folders untouched for >14 days
./DevPurge.Cli --path ~/repos --clean --min-age 14
```

---

## 🔐 SHA-256 Checksums

```
D03B285E3501EC16660A31691186F9258011B2E0F36C0542B9277DD773B57C5E  DevPurge-v1.1.0-windows-x64-installer.exe
2F65988D6CD3BADC656EDB08F8D04E39A7DB9AA23004F0F6F35D0263CAC9F9F9  DevPurge-v1.1.0-windows-x64-gui.zip
6CC5E3E04F46527E7B3ED047087BE006E66D9D6792A6DCC4FF01D84DD44F0C30  DevPurge-v1.1.0-windows-x64-cli.zip
14E248FF685C53CFE52D158DB697301EB5E17DB456770A95103B86D80E6BCF54  DevPurge-v1.1.0-linux-x64-cli.tar.gz
23FC822B1ABEBA0963E297C708BA96D734C076A5C56AEFEE4B8992403A4AF0EB  DevPurge-v1.1.0-linux-arm64-cli.tar.gz
```

---

## 💖 Support & Donations

If DevPurge helped you reclaim tens of gigabytes of SSD space:
- [💖 GitHub Sponsors (@Behrad87)](https://github.com/sponsors/Behrad87)
- [☕ Buy Me a Coffee on Ko-fi](https://ko-fi.com/behrad87)
- [🇮🇷 حمایت از طریق ری‌میت (شتاب)](https://reymit.ir/behrad87)
