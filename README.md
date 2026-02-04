# FCP Mod Updater

A command-line tool for managing [Fallout Collaboration Project](https://github.com/FalloutCollaborationProject) RimWorld mods. Features both interactive and automated workflows for discovering, updating, installing, and managing FCP mods.

## Features

- **Auto-discover** RimWorld installation across Steam, GOG, and Lutris on Windows, Linux, and macOS
- **Interactive mode** with rich terminal UI for browsing, updating, and managing mods
- **Batch update mode** for automation scripts
- **Install new mods** directly from the FCP GitHub organization
- **Convert local mods** (ZIP downloads) to Git repositories for easy updates
- **Branch/commit switching** for testing specific mod versions
- **Status overview** showing update availability, local changes, and sync state

## Requirements

- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download) or later (Except for Self-Contained Releases)
- [Git](https://git-scm.com/downloads) installed and available in PATH

## Installation

### Pre-Compiled Releases

Download from the [Releases](https://github.com/FalloutCollaborationProject/FCPModUpdater/releases) page:

| Archive | Description |
|---------|-------------|
| `*-selfcontained.zip/.tar.gz` | Standalone, no .NET required |
| `*-win-x64.zip` / `*-linux-x64.tar.gz` | Smaller, requires .NET 10 Runtime |

### Build from Source
```bash
git clone https://github.com/FalloutCollaborationProject/FCPModUpdater.git
cd FCPModUpdater
dotnet build
```

## Usage

### Cli Note

You may choose between either `dotnet run -- ARGS` or running the built application directly with the args
(ex: `FCPModUpdater scan`)

### Interactive Mode (scan arg, Default)

Launches the interactive menu where you can:
- View status of all installed FCP mods
- Update mods with available changes
- Install new mods from FCP
- Uninstall mods
- Convert local (non-git) mods to git repositories
- Switch mod versions (branches/commits)


#### Double Click

- **Windows:** Double-click the `.exe` to launch the interactive menu directly — no command line needed.

- **Linux:** The archive includes `fcp-mod-manager.desktop` for desktop integration. Copy it to `~/.local/share/applications/` and update the `Exec` path to point to the extracted binary.

### Batch Update Mode

```bash
dotnet run -- update
```

Non-interactive mode that updates all FCP mods with available changes. Ideal for automation and scheduled tasks. Returns exit code 0 on success, 1 if any updates fail.

### Options

Both commands accept:
- `-d, --directory <path>` — Path to RimWorld Mods folder (auto-discovers if omitted)

```bash
dotnet run -- scan --directory "/path/to/RimWorld/Mods"
dotnet run -- update -d "C:\Games\RimWorld\Mods"
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `GITHUB_TOKEN` | Optional. GitHub personal access token to increase API rate limit from 60 to 5000 requests/hour |

## How Mods Are Identified

FCP mods are identified by:
1. Git remote URL containing `github.com/FalloutCollaborationProject/`
2. Folder name matching an FCP repository (supports ZIP downloads with branch suffixes like `-main`)

Only repositories tagged with the `rimworld-mod` topic are included.

## Status Icons

| Icon | Status | Description |
|------|--------|-------------|
| ✓ | Up to Date | No updates available |
| ↓ | Behind | Updates available |
| ↑ | Ahead | Local commits not on remote |
| ⇅ | Diverged | Both behind and ahead |
| ~ | Modified | Has uncommitted local changes |
| — | Not Git | Local folder (not a git repo) |
| ✗ | Error | Git operation failed |
