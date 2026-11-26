## Which file do I download?

Every download also needs [Git](https://git-scm.com/downloads) installed.

| You want | Windows | Linux |
|---|---|---|
| **Desktop app (GUI)**: recommended for most users, updates itself | `CollabProjectUpdater-win-x64-Setup.exe` | `CollabProjectUpdater-x86_64.AppImage` |
| Desktop app without installing, updates itself | `CollabProjectUpdater-win-x64-Portable.zip` | |
| Command-line (CLI) app | `CollabProjectUpdater-CLI-win-x64-selfcontained.zip` | `CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz` |
| Updater script for the CLI (put it in the CLI's folder) | `update-collab-project-updater.bat` | `update-collab-project-updater.sh` |

Installed with `Setup.exe`, the AppImage or the portable zip? Click **Update now** in the app's update banner.

Still on a plain desktop-app folder (the `CollabProjectUpdater-win-x64.zip` / `-linux-x64.tar.gz` archives, no longer published) or the old AppImage? Click **Get the installer** in the banner to switch once (settings are kept). Or download the updater script from this release into that folder and run it: it switches the folder to the portable zip (Windows) or the AppImage (Linux), which update themselves from then on. The copy of the script already in your folder can't, because it looks for the old archives. The script also updates the CLI and migrates installs from before the rename to Collaboration Project Updater.

**AppImage:** make it executable (`chmod +x CollabProjectUpdater-x86_64.AppImage`) and run it. It updates itself from the app; AppImages from before this release don't, so download the new one once.

The `.nupkg` and `releases.*.json` files are used by the in-app updater; you don't need to download them.

`checksums.txt` lists the SHA-256 of every archive, the AppImage and the Windows installer files.

---
