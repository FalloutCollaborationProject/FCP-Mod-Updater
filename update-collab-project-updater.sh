#!/usr/bin/env bash
set -euo pipefail

BASE_URL="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/latest/download"
RELEASES_API="https://api.github.com/repos/FalloutCollaborationProject/FCP-Mod-Updater/releases?per_page=50"

if [[ "${1:-}" != "--from-temp" ]]; then
    temp_script="$(mktemp)"
    cp "$0" "${temp_script}"
    set +e
    bash "${temp_script}" --from-temp
    exit_code=$?
    set -e
    rm -f "${temp_script}"
    exit "${exit_code}"
fi

# The same script updates the GUI (CollabProjectUpdater) and the CLI (CollabProjectUpdater-CLI); the install folder says which.
# Installs from before the rename (FCPModManager / FCPModUpdater) are migrated: the old files are removed after updating.
OLD_FILES=()
if [[ -f "./CollabProjectUpdater" ]]; then
    APP_NAME="CollabProjectUpdater"
    ARCHIVE="CollabProjectUpdater-linux-x64.tar.gz"
    ARCHIVE_DIR="gui-linux-x64"
elif [[ -f "./CollabProjectUpdater-CLI" ]]; then
    APP_NAME="CollabProjectUpdater-CLI"
    ARCHIVE="CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz"
    ARCHIVE_DIR="linux-x64-sc"
elif [[ -f "./FCPModManager" ]]; then
    APP_NAME="FCPModManager"
    ARCHIVE="CollabProjectUpdater-linux-x64.tar.gz"
    ARCHIVE_DIR="gui-linux-x64"
    OLD_FILES=("./FCPModManager" "./fcp_logo.png" "./fcp-mod-manager-gui.desktop" "./update-fcp-mod-manager.sh")
elif [[ -f "./FCPModUpdater" ]]; then
    APP_NAME="FCPModUpdater"
    ARCHIVE="CollabProjectUpdater-CLI-linux-x64-selfcontained.tar.gz"
    ARCHIVE_DIR="linux-x64-sc"
    OLD_FILES=("./FCPModUpdater" "./fcp-mod-manager.desktop" "./update-fcp-mod-manager.sh")
else
    echo "Run this script from the existing Collaboration Project Updater install folder."
    exit 1
fi

if pgrep -x "${APP_NAME}" >/dev/null 2>&1; then
    echo "Collaboration Project Updater is still running. Close it before updating."
    exit 1
fi

for tool in curl tar sha256sum; do
    if ! command -v "${tool}" >/dev/null 2>&1; then
        echo "Missing required tool: ${tool}"
        exit 1
    fi
done

if [[ ! -w "." ]]; then
    echo "This install folder is not writable. Move the app to a writable folder or update manually."
    exit 1
fi

staging="$(mktemp -d)"
cleanup() {
    rm -rf "${staging}"
}
trap cleanup EXIT

echo "Select update channel:"
echo "  1) Latest stable release"
echo "  2) Latest pre-release"
read -r -p "Choice [1]: " channel_choice

case "${channel_choice:-1}" in
    1)
        echo "Downloading latest stable Collaboration Project Updater release..."
        curl -fsSL "${BASE_URL}/${ARCHIVE}" -o "${staging}/${ARCHIVE}"
        curl -fsSL "${BASE_URL}/checksums.txt" -o "${staging}/checksums.txt"
        ;;
    2)
        if ! command -v python3 >/dev/null 2>&1; then
            echo "Missing required tool for pre-release selection: python3"
            exit 1
        fi

        echo "Finding latest Collaboration Project Updater pre-release..."
        curl -fsSL "${RELEASES_API}" -o "${staging}/releases.json"
        mapfile -t asset_urls < <(python3 - "${ARCHIVE}" "${staging}/releases.json" <<'PY'
import json
import sys

archive_name = sys.argv[1]
releases_path = sys.argv[2]

with open(releases_path, "r", encoding="utf-8") as handle:
    releases = json.load(handle)

release = next((item for item in releases if item.get("prerelease") and not item.get("draft")), None)
if release is None:
    sys.exit("No pre-release was found.")

assets = release.get("assets", [])
archive = next((asset for asset in assets if asset.get("name") == archive_name), None)
checksums = next((asset for asset in assets if asset.get("name") == "checksums.txt"), None)

if archive is None:
    sys.exit(f"No asset named {archive_name} was found on {release.get('tag_name', 'the pre-release')}.")
if checksums is None:
    sys.exit(f"No checksums.txt asset was found on {release.get('tag_name', 'the pre-release')}.")

print(archive["browser_download_url"])
print(checksums["browser_download_url"])
PY
)

        if [[ "${#asset_urls[@]}" -ne 2 ]]; then
            echo "Could not resolve pre-release download URLs."
            exit 1
        fi

        echo "Downloading latest pre-release Collaboration Project Updater release..."
        curl -fsSL "${asset_urls[0]}" -o "${staging}/${ARCHIVE}"
        curl -fsSL "${asset_urls[1]}" -o "${staging}/checksums.txt"
        ;;
    *)
        echo "Invalid selection."
        exit 1
        ;;
esac

cd "${staging}"
if ! grep " ${ARCHIVE}$" checksums.txt | sha256sum -c -; then
    echo "Checksum verification failed. Update was not installed."
    exit 1
fi

tar -xzf "${ARCHIVE}"
source_dir="${staging}/${ARCHIVE_DIR}"

if [[ ! -d "${source_dir}" ]]; then
    echo "Downloaded archive did not contain the expected ${ARCHIVE_DIR} folder."
    exit 1
fi

cd - >/dev/null

echo "Installing update..."
cp -R "${source_dir}/." .
if [[ ${#OLD_FILES[@]} -gt 0 ]]; then
    APP_NAME="${APP_NAME/FCPModManager/CollabProjectUpdater}"
    APP_NAME="${APP_NAME/FCPModUpdater/CollabProjectUpdater-CLI}"
    rm -f "${OLD_FILES[@]}"
    echo "Renamed: start ./${APP_NAME} from now on."
fi
chmod +x "./${APP_NAME}" "./update-collab-project-updater.sh"

echo "Update complete. Start Collaboration Project Updater normally."
