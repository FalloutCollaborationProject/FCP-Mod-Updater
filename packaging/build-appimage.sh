#!/usr/bin/env bash
# Packs a self-contained GUI publish folder into an AppImage.
# Usage: build-appimage.sh <gui-publish-dir> <out-dir>
# Used by publish-all.sh and .github/workflows/release.yml.
set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "Usage: $0 <gui-publish-dir> <out-dir>"
    exit 1
fi

repo="$(cd "$(dirname "$0")/.." && pwd)"
publish_dir="$(cd "$1" && pwd)"
mkdir -p "$2"
out_dir="$(cd "$2" && pwd)"

BIN_NAME="CollabProjectUpdater"
APP_ID="collab-project-updater"
APPIMAGE_NAME="CollabProjectUpdater-x86_64.AppImage"
UPDATE_INFO="gh-releases-zsync|FalloutCollaborationProject|FCP-Mod-Updater|latest|CollabProjectUpdater-*x86_64.AppImage.zsync"
TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"

if [[ ! -f "${publish_dir}/${BIN_NAME}" ]]; then
    echo "No ${BIN_NAME} in ${publish_dir}. Publish the GUI for linux-x64 first."
    exit 1
fi

cache="${XDG_CACHE_HOME:-$HOME/.cache}/collab-project-updater-build"
tool="${cache}/appimagetool-x86_64.AppImage"
if [[ ! -x "${tool}" ]]; then
    echo "Downloading appimagetool..."
    mkdir -p "${cache}"
    curl -fsSL "${TOOL_URL}" -o "${tool}.part"
    chmod +x "${tool}.part"
    mv "${tool}.part" "${tool}"
fi

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
appdir="${work}/${BIN_NAME}.AppDir"
icon_dir="${appdir}/usr/share/icons/hicolor/256x256/apps"
mkdir -p "${appdir}/usr/bin" "${icon_dir}"

cp "${publish_dir}/${BIN_NAME}" "${appdir}/usr/bin/${BIN_NAME}"
chmod +x "${appdir}/usr/bin/${BIN_NAME}"

logo="${repo}/FCPModUpdater.Gui/Assets/collab_logo.png"
cp "${logo}" "${appdir}/${APP_ID}.png"
cp "${logo}" "${appdir}/.DirIcon"
cp "${logo}" "${icon_dir}/${APP_ID}.png"

cat > "${appdir}/${APP_ID}.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Collaboration Project Updater
Comment=Manage RimWorld mods from the Fallout and Elder Scrolls Collaboration Projects
Exec=${BIN_NAME}
Icon=${APP_ID}
Terminal=false
Categories=Game;Utility;
DESKTOP

cat > "${appdir}/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "${HERE}/usr/bin/CollabProjectUpdater" "$@"
APPRUN
chmod +x "${appdir}/AppRun"

# With update information appimagetool also writes the .zsync file, which needs zsyncmake.
args=()
if command -v zsyncmake >/dev/null 2>&1; then
    args+=(-u "${UPDATE_INFO}")
else
    echo "zsyncmake not found: building without update information (no .zsync file)."
fi

# appimagetool writes the .zsync next to the working directory, so build inside the output folder.
cd "${out_dir}"
rm -f "${APPIMAGE_NAME}" "${APPIMAGE_NAME}.zsync"
ARCH=x86_64 "${tool}" --appimage-extract-and-run "${args[@]}" "${appdir}" "${APPIMAGE_NAME}"
chmod +x "${APPIMAGE_NAME}"
echo "Built ${out_dir}/${APPIMAGE_NAME}"
