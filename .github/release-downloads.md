## Which file do I download?

Every download also needs [Git](https://git-scm.com/downloads) installed.

| You want | Windows | Linux |
|---|---|---|
| **Desktop app (GUI)**: recommended for most users, updates itself | `CollabProjectUpdater-win-x64-Setup.exe` | `CollabProjectUpdater-x86_64.AppImage` |
| Desktop app without installing, updates itself | `CollabProjectUpdater-win-x64-Portable.zip` | |
| Desktop app as a plain folder (updater script) | `CollabProjectUpdater-win-x64.zip` | `CollabProjectUpdater-linux-x64.tar.gz` |
| Command-line (CLI) app | `CollabProjectUpdater-CLI-win-x64-selfcontained.zip` | `CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz` |
| CLI, smaller download (needs the [.NET 10 Runtime](https://dotnet.microsoft.com/download)) | `CollabProjectUpdater-CLI-win-x64.zip` | `CollabProjectUpdater-CLI-linux-x64.tar.gz` |

Installed with `Setup.exe`, the AppImage or the portable zip? Click **Update now** in the app's update banner. Using a plain folder or the old AppImage? Click **Get the installer** in the banner to switch once (settings are kept), or run `update-collab-project-updater.bat` (Windows) or `update-collab-project-updater.sh` (Linux) from your install folder. It updates whichever app is there, and also migrates installs from before the rename to Collaboration Project Updater (old `update-fcp-mod-manager` scripts can't: download the new script from this release once).

**AppImage:** make it executable (`chmod +x CollabProjectUpdater-x86_64.AppImage`) and run it. It updates itself from the app; AppImages from before this release don't, so download the new one once.

The `.nupkg` and `releases.*.json` files are used by the in-app updater; you don't need to download them.

`checksums.txt` lists the SHA-256 of every archive.

---
