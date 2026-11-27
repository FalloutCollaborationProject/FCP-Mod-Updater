# Changelog

All notable changes to Collaboration Project Updater. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); headings match the release tags without the `v`.

Write each change once, under `[Unreleased]`. When tagging, rename `[Unreleased]` to the tag's version and date and add a new empty `[Unreleased]` above it. A stable release's notes automatically include the sections of its pre-releases (`.github/changelog-notes.sh`).

## [Unreleased]

## [1.0.0] - 2026-11-27

First stable release of Collaboration Project Updater, the desktop app and the command-line (CLI) app. No changes since 1.0.0-beta.2; the notes below cover everything since 0.4.2.

## [1.0.0-beta.2] - 2026-11-27

### Changed
- Updates now run in parallel from 2 mods (was 3 or more).
- The parallel updates limit is now your CPU's thread count, up to 16 (was a fixed 8). Applies to the Settings page, `settings set update-parallelism` and `update -j`.

## [1.0.0-beta.1] - 2026-11-27

First release since 0.4.2. The app is now called Collaboration Project Updater and comes as a desktop app as well as the command-line (CLI) app.

### Added
- Desktop app (GUI) for Windows and Linux: Installed mods table with status, updates and branch/commit pickers; Install and Migrate pages; Versions page; Settings page; live git output. Download `CollabProjectUpdater-win-x64-Setup.exe` (or the portable zip) on Windows, `CollabProjectUpdater-x86_64.AppImage` on Linux.
- In-app updates: the desktop app downloads and installs new versions itself (**Update now** on the Updates page). The Updates page also shows the release notes and has switches for checking on launch and including pre-releases.
- Elder Scrolls Collaboration Project (ESCP) mods, next to FCP. Mods are grouped by project, each project can be turned off (Settings page, or `settings set category.<id>.enabled false`), and "Select all recommended" works per project.
- Game-version branches: when a mod has a `rimworld-X.Y` branch for your RimWorld version (read from `Version.txt`, or set with `settings set game-version`), installs use it, and mods on the wrong branch are flagged and can be switched. `update --allow-mainline` updates them anyway.
- Updates of 3 or more mods run in parallel (default 4 at a time, up to 8): Settings page, `settings set update-parallelism`, or `update -j`.
- Saved settings, shared by the CLI and the desktop app: Mods folder, GitHub token, update checks. The CLI has a `settings` command (`settings set|unset <key> [value]`) and a settings menu.
- `--github-token <token>` for a single run. Token order: `--github-token`, then `GITHUB_TOKEN`, then the saved token. A token also lets the app see the organisation's private repositories.
- Git location setting for a Git that isn't on PATH; on Windows the usual Git for Windows folders are found automatically.
- The CLI shows a one-time notice that the desktop app exists.

### Changed
- Renamed from FCP Mod Manager to Collaboration Project Updater. The CLI is now `CollabProjectUpdater-CLI` (`CollabProjectUpdater-CLI-<platform>-selfcontained` downloads) and the updater scripts are `update-collab-project-updater.sh`/`.bat`. The old `update-fcp-mod-manager.*` scripts can't find the new release: download the new script into your old folder once and run it; it installs the renamed CLI there and removes the old files.
- `-d|--directory` now overrides the saved Mods folder for that run, and a folder you pick is remembered.
- Clearer warning when Git isn't found at startup.

### Removed
- The framework-dependent CLI downloads (`FCPModUpdater-win-x64.zip` / `-linux-x64.tar.gz`); use the self-contained ones.

### Fixed
- Leftover folders of removed git submodules no longer make a mod show as modified.

---

Versions before 1.0 (the CLI only) are listed on the [GitHub Releases page](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases).
