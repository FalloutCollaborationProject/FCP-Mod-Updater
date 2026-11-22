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

# GUI AppImage (single-file Linux download)
./packaging/build-appimage.sh "$OUT/gui-linux-x64" "$OUT"

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
sha256sum *.zip *.tar.gz *.AppImage > checksums.txt
cd ..

# Single-file publish adds Microsoft.NET.ILLink.Tasks to the lock files; don't leave that behind.
git checkout -- FCPModUpdater.Core/packages.lock.json FCPModUpdater.Gui/packages.lock.json packages.lock.json 2>/dev/null || true

echo "Archives:"; ls -1 "$OUT"
