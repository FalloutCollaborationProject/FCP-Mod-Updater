#!/usr/bin/env bash
# Local build of every release archive into ./publish (mirrors .github/workflows/release.yml).
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="CollabProjectUpdater-CLI"   # CLI
GUI_NAME="CollabProjectUpdater"   # Avalonia GUI
CLI_PROJECT="./FCPModUpdater.csproj"
GUI_PROJECT="./FCPModUpdater.Gui/FCPModUpdater.Gui.csproj"
OUT="./publish"

for tool in dotnet zip tar sha256sum curl; do
    command -v "$tool" >/dev/null || { echo "Missing required tool: $tool"; exit 1; }
done

rm -rf "$OUT"
mkdir -p "$OUT"

publish() { # project rid self-contained out-dir
    local single=false
    [[ "$3" == true ]] && single=true
    dotnet publish "$1" -c Release -r "$2" --self-contained "$3" -p:PublishSingleFile="$single" -o "$OUT/$4"
}

# CLI, self-contained single file
publish "$CLI_PROJECT" win-x64 true win-x64-sc
publish "$CLI_PROJECT" linux-x64 true linux-x64-sc
cp ./update-collab-project-updater.bat "$OUT/win-x64-sc/"
cp ./update-collab-project-updater.sh ./collab-project-updater.desktop "$OUT/linux-x64-sc/"

# CLI, framework-dependent
publish "$CLI_PROJECT" win-x64 false win-x64-fd
publish "$CLI_PROJECT" linux-x64 false linux-x64-fd
cp ./collab-project-updater.desktop "$OUT/linux-x64-fd/"

# GUI, self-contained single file
publish "$GUI_PROJECT" win-x64 true gui-win-x64
publish "$GUI_PROJECT" linux-x64 true gui-linux-x64
cp ./update-collab-project-updater.bat "$OUT/gui-win-x64/"
cp ./update-collab-project-updater.sh ./collab-project-updater-gui.desktop ./FCPModUpdater.Gui/Assets/collab_logo.png "$OUT/gui-linux-x64/"

# GUI installers (Velopack): Windows Setup.exe/Portable.zip and the self-updating Linux AppImage.
# Channels and flags mirror release.yml; needs vpk (dotnet tool install -g vpk) and mksquashfs (squashfs-tools).
VERSION="$(git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//')"
VERSION="${VERSION:-0.0.1}"
if command -v vpk >/dev/null && command -v mksquashfs >/dev/null; then
    rm -rf "$OUT/vp"
    dotnet publish "$GUI_PROJECT" -c Release -r win-x64 --self-contained true -o "$OUT/vp/win-x64"
    dotnet publish "$GUI_PROJECT" -c Release -r linux-x64 --self-contained true -o "$OUT/vp/linux-x64"
    vpk "[win]" pack -u "$GUI_NAME" -v "$VERSION" -c win-x64 -p "$OUT/vp/win-x64" -e "$GUI_NAME.exe" \
        -i ./FCPModUpdater.Gui/Assets/app.ico --packTitle "Collaboration Project Updater" \
        --packAuthors "Fallout Collaboration Project" --shortcuts Desktop,StartMenuRoot -o "$OUT/velopack"
    vpk pack -u "$GUI_NAME" -v "$VERSION" -c linux-x64 -p "$OUT/vp/linux-x64" -e "$GUI_NAME" \
        -i ./FCPModUpdater.Gui/Assets/collab_logo.png --packTitle "Collaboration Project Updater" \
        --packAuthors "Fallout Collaboration Project" --categories Utility -o "$OUT/velopack"
    mv "$OUT"/velopack/*.AppImage "$OUT/${GUI_NAME}-x86_64.AppImage"
    rm -rf "$OUT/vp"
else
    echo "Skipping Velopack installers: needs vpk and mksquashfs on PATH."
fi

# Standalone updater scripts are release assets too
cp ./update-collab-project-updater.bat ./update-collab-project-updater.sh "$OUT/"

cd "$OUT"
chmod +x linux-x64-sc/"$APP_NAME" linux-x64-sc/update-collab-project-updater.sh gui-linux-x64/"$GUI_NAME" gui-linux-x64/update-collab-project-updater.sh
zip -qr "${APP_NAME}-win-x64-selfcontained.zip" win-x64-sc
tar -czf "${APP_NAME}-linux-x64-selfcontained.tar.gz" linux-x64-sc
zip -qr "${APP_NAME}-win-x64.zip" win-x64-fd
tar -czf "${APP_NAME}-linux-x64.tar.gz" linux-x64-fd
zip -qr "${GUI_NAME}-win-x64.zip" gui-win-x64
tar -czf "${GUI_NAME}-linux-x64.tar.gz" gui-linux-x64
rm -rf win-x64-sc linux-x64-sc win-x64-fd linux-x64-fd gui-win-x64 gui-linux-x64
sha256sum *.zip *.tar.gz $(ls *.AppImage velopack/*-Setup.exe velopack/*-Portable.zip 2>/dev/null) | sed 's| velopack/| |' > checksums.txt
cd ..

# Single-file publish adds Microsoft.NET.ILLink.Tasks to the lock files; don't leave that behind.
git checkout -- FCPModUpdater.Core/packages.lock.json FCPModUpdater.Gui/packages.lock.json packages.lock.json 2>/dev/null || true

echo "Archives:"; ls -1 "$OUT"
