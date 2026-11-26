## Which file do I download?

Every download also needs [Git](https://git-scm.com/downloads) installed.

| You want | Windows | Linux |
|---|---|---|
| **Desktop app (GUI)**: recommended for most users, updates itself | [`CollabProjectUpdater-win-x64-Setup.exe`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/CollabProjectUpdater-win-x64-Setup.exe) | [`CollabProjectUpdater-x86_64.AppImage`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/CollabProjectUpdater-x86_64.AppImage) |
| Desktop app without installing, updates itself | [`CollabProjectUpdater-win-x64-Portable.zip`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/CollabProjectUpdater-win-x64-Portable.zip) | |
| Command-line (CLI) app | [`CollabProjectUpdater-CLI-win-x64-selfcontained.zip`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/CollabProjectUpdater-CLI-win-x64-selfcontained.zip) | [`CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz) |
| Updater script for the CLI (put it in the CLI's folder) | [`update-collab-project-updater.bat`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/update-collab-project-updater.bat) | [`update-collab-project-updater.sh`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/update-collab-project-updater.sh) |

Installed with `Setup.exe`, the AppImage or the portable zip? Click **Update now** in the app's update banner.

**Coming from 0.4.x (FCP Mod Manager, CLI)?** Download the updater script for your platform from this release into your old folder and run it once: it installs the renamed CLI there and removes the old files. The `update-fcp-mod-manager.*` script already in that folder can't find this release.

On a test build of the desktop app (a plain zip/tar.gz folder or an older AppImage)? Click **Get the installer** in the banner to switch once; your settings are kept.

**AppImage:** make it executable (`chmod +x CollabProjectUpdater-x86_64.AppImage`) and run it.

The `.nupkg` and `releases.*.json` files are used by the in-app updater; you don't need to download them.

[`checksums.txt`](https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/download/@TAG@/checksums.txt) lists the SHA-256 of every archive, the AppImage and the Windows installer files.

---
