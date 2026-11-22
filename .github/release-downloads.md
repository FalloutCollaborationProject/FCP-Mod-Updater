## Which file do I download?

Every download also needs [Git](https://git-scm.com/downloads) installed.

| You want | Windows | Linux |
|---|---|---|
| **Desktop app (GUI)**: recommended for most users | `CollabProjectUpdater-win-x64.zip` | `CollabProjectUpdater-linux-x64.tar.gz` |
| Desktop app as a single file | | `CollabProjectUpdater-x86_64.AppImage` |
| Command-line (CLI) app | `CollabProjectUpdater-CLI-win-x64-selfcontained.zip` | `CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz` |
| CLI, smaller download (needs the [.NET 10 Runtime](https://dotnet.microsoft.com/download)) | `CollabProjectUpdater-CLI-win-x64.zip` | `CollabProjectUpdater-CLI-linux-x64.tar.gz` |

Already installed? Run `update-collab-project-updater.bat` (Windows) or `update-collab-project-updater.sh` (Linux) from your install folder. It updates whichever app is there, and also migrates installs from before the rename to Collaboration Project Updater (old `update-fcp-mod-manager` scripts can't: download the new script from this release once).

**AppImage:** make it executable (`chmod +x CollabProjectUpdater-x86_64.AppImage`) and run it. To update, download the new file, or use an AppImage updater such as AppImageUpdate or Gear Lever (they use the `.zsync` file).

`checksums.txt` lists the SHA-256 of every archive.

---
