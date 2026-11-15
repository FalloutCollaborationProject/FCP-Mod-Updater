<h1 align="center">FCP Mod Updater</h1>

<p align="center">
  <img src="fcp_logo.png" alt="FCP logo" width="180" />
</p>

<p align="center">
  <strong>A CLI tool for managing Fallout Collaboration Project RimWorld mods.</strong>
</p>

<p align="center">
  List, install, update, and manage FCP mods with an interactive terminal UI.
</p>

<p align="center">
  <a href="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/latest">
    <img src="https://img.shields.io/github/v/release/FalloutCollaborationProject/FCP-Mod-Updater?style=for-the-badge&label=Stable&color=brightgreen" alt="Stable Release">
  </a>
  <a href="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases">
    <img src="https://img.shields.io/github/v/release/FalloutCollaborationProject/FCP-Mod-Updater?include_prereleases&style=for-the-badge&label=Beta&color=orange" alt="Beta Release">
  </a>
  <a href="https://discord.gg/XcgBf3AbPa">
    <img src="https://img.shields.io/badge/Discord-Join%20Us-7289da?style=for-the-badge&logo=discord&logoColor=white" alt="Discord">
  </a>
</p>

<p align="center">
  <a href="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/stargazers">
    <img src="https://img.shields.io/github/stars/FalloutCollaborationProject/FCP-Mod-Updater?style=flat-square" alt="Stars">
  </a>
  <a href="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases">
    <img src="https://img.shields.io/github/downloads/FalloutCollaborationProject/FCP-Mod-Updater/total?style=flat-square" alt="Downloads">
  </a>
  <!-- <a href="LICENSE">
    <img src="https://img.shields.io/github/license/FalloutCollaborationProject/FCP-Mod-Updater?style=flat-square" alt="License">
  </a> -->
  <a href="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/actions/workflows/ci.yml">
    <img src="https://img.shields.io/github/actions/workflow/status/FalloutCollaborationProject/FCP-Mod-Updater/ci.yml?style=flat-square&label=Tests" alt="Tests">
  </a>
</p>

<p align="center">
  <img src="./demo.png" alt="FCP Mod Updater preview" />
</p>

## Features

- **Auto-discover** RimWorld installation across Steam, GOG, and Lutris on Windows, Linux, and macOS
- **Interactive mode** with rich terminal UI for browsing, updating, and managing mods
- **Batch update mode** for automation scripts
- **Install new mods** directly from the FCP GitHub organization
- **Convert local mods** (ZIP downloads) to Git repositories for easy updates
- **Branch/commit switching** for testing specific mod versions
- **Status overview** showing update availability, local changes, and sync state

## Requirements

- [Git](https://git-scm.com/downloads) installed and available in PATH

## Installation

### Standard Installation

> [!NOTE]
> Your anti-virus may flag the download as suspicious/malicious, this is due to the code being unsigned. To remedy this would cost us real money and not a small amount either, you may check on [VirusTotal](https://www.virustotal.com/gui/) or [build from source.](#build-from-source)

> [!TIP]
> **Not sure which file to download?** Grab the **GUI** archive for your platform (`FCPModManager-GUI-*`). It's the desktop app, and it needs nothing installed apart from Git.

| Archive | What it is | Who it's for |
|---------|------------|--------------|
| `FCPModManager-GUI-win-x64.zip` / `FCPModManager-GUI-linux-x64.tar.gz` | Desktop app, everything bundled | **Most users**: download this one |
| `FCPModManager-GUI-x86_64.AppImage` | Desktop app for Linux as one file: `chmod +x` it and run | Linux users who prefer an AppImage |
| `FCPModUpdater-*-selfcontained.zip` / `.tar.gz` | Command-line app, everything bundled | Terminal users |
| `FCPModUpdater-win-x64.zip` / `FCPModUpdater-linux-x64.tar.gz` | Command-line app, smaller, requires the [.NET 10 Runtime](https://dotnet.microsoft.com/download) | Users who already have .NET installed |

Downloads are available on the [Releases](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases) page.

### Updating the App

FCP Mod Manager checks for newer app releases and prints update instructions when one is available. The recommended update path is still to download the latest self-contained archive from the [Releases](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases) page and replace your existing app folder.

For convenience, self-contained releases also include manual updater scripts:

| Platform | Script |
|----------|--------|
| Windows | `update-fcp-mod-manager.bat` |
| Linux | `./update-fcp-mod-manager.sh` |

Before running an updater script, close FCP Mod Manager and run the script from the existing app folder. The script will ask whether to use the latest stable release or the latest pre-release, download the matching archive and `checksums.txt`, verify the SHA256 checksum, then replace the files in the current folder.

> [!WARNING]
> The updater scripts are mainly convenience helpers. If an update fails, download the release archive manually and replace the app folder yourself.

> [!WARNING]
> The Windows updater script is less tested than the Linux script. If it fails, use the manual download method from the Releases page.

> [!NOTE]
> The Linux updater requires `curl`, `tar`, and `sha256sum`. Choosing the pre-release channel also requires `python3` for GitHub release JSON parsing.


### Build from Source
```bash
git clone https://github.com/FalloutCollaborationProject/FCPModUpdater.git
cd FCPModUpdater
dotnet build
```

## Status Icons (CLI)

| Icon | Status | Description |
|------|--------|-------------|
| ✓ | Up to Date | No updates available |
| ↓ | Behind | Updates available |
| ↑ | Ahead | Local commits not on remote |
| ⇅ | Diverged | Both behind and ahead |
| ~ | Modified | Has uncommitted local changes |
| — | Not Git | Local folder (not a git repo) |
| ✗ | Error | Git operation failed |
