# Changelog

All notable changes to Collaboration Project Updater. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); headings match the release tags without the `v`.

Write each change once, under `[Unreleased]`. When tagging, rename `[Unreleased]` to the tag's version and date and add a new empty `[Unreleased]` above it. A stable release's notes automatically include the sections of its pre-releases (`.github/changelog-notes.sh`).

## [Unreleased]

## [1.0.0-test.4] - 2026-11-27

### Fixed
- Windows updater script failed right away with `'""' is not recognized as an internal or external command`.

### Added
- Elder Scrolls Collaboration Project (ESCP) mods, next to FCP. Mods are grouped by project, and each project can be turned off in Settings (CLI: `settings set category.<id>.enabled false`).
- Install page: "Select all recommended" per project.
- In-app updates: installed with `Setup.exe`, the portable zip or the new AppImage, the app downloads and applies a new version itself (**Update now**).
- Updates page: current version, release check and notes, whether to check on launch and whether to include pre-releases.
- Git location setting, for a Git that isn't on PATH. On Windows the usual Git for Windows install folders are found automatically.
- Windows app icon.

### Changed
- Renamed from FCP Mod Manager to Collaboration Project Updater. Settings and existing installs carry over; the updater scripts migrate old folders.
- Clearer warning when Git isn't found at startup.
- Project headers stay visible while scrolling the Installed table.
- The window opens larger, sized to the primary monitor.
- Fewer release files: the desktop app ships only as `Setup.exe`, the portable zip and the AppImage, and the CLI only as the self-contained build.
- The updater scripts are separate downloads. In an old desktop-app folder they switch it to the self-updating version.
- Pre-release versions compare correctly (`1.0.0-test.2` is newer than `1.0.0-test.1`), and the version number wraps instead of being cut off.

### Fixed
- Uninstalling mods from the desktop app on Windows.
- Lutris is no longer mentioned on Windows.
- The no-Git warning no longer overflows the window.

## [1.0.0-alpha.3] - 2026-11-16

### Added
- Updates of 3 or more mods run in parallel (default 4 at a time, up to 8). Settings page, `settings set update-parallelism`, or `update -j`.
- The CLI shows a one-time notice that a desktop version exists.

### Fixed
- A parallel update could stop early.

## [1.0.0-alpha.1] - 2026-11-15

### Added
- Game-version branches: when a mod has a `rimworld-X.Y` branch for your RimWorld version (read from `Version.txt`, or set with `settings set game-version`), installs use it and mods on the wrong branch are flagged and can be switched. The CLI's `update --allow-mainline` updates them anyway.
- Linux AppImage of the desktop app.

### Fixed
- Leftover folders of removed git submodules no longer make a mod show as modified.

## [1.0.0-dev.2] - 2026-11-14

### Added
- Desktop app (GUI) for Windows and Linux: Installed mods table with status, update, branch and commit pickers; Install and Migrate pages; Versions page; Settings page; live git output.
- Settings shared by the CLI and the desktop app (Mods folder, GitHub token, update checks), editable in the CLI with `settings` and from its interactive menu.
- A GitHub token can see the organisation's private repositories; changing it rescans.

---

Versions before 1.0 (the CLI only) are listed on the [GitHub Releases page](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases).
