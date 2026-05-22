#!/usr/bin/env bash
set -euo pipefail

APP_NAME="FCPModUpdater"
ARCHIVE="FCPModUpdater-linux-x64-selfcontained.tar.gz"
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

if [[ ! -f "./${APP_NAME}" ]]; then
    echo "Run this script from the existing FCP Mod Manager install folder."
    exit 1
fi

if pgrep -x "${APP_NAME}" >/dev/null 2>&1; then
    echo "FCP Mod Manager is still running. Close it before updating."
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
        echo "Downloading latest stable FCP Mod Manager release..."
        curl -fsSL "${BASE_URL}/${ARCHIVE}" -o "${staging}/${ARCHIVE}"
        curl -fsSL "${BASE_URL}/checksums.txt" -o "${staging}/checksums.txt"
        ;;
    2)
        if ! command -v python3 >/dev/null 2>&1; then
            echo "Missing required tool for pre-release selection: python3"
            exit 1
        fi

        echo "Finding latest FCP Mod Manager pre-release..."
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

        echo "Downloading latest pre-release FCP Mod Manager release..."
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
source_dir="${staging}/linux-x64-sc"

if [[ ! -d "${source_dir}" ]]; then
    echo "Downloaded archive did not contain the expected linux-x64-sc folder."
    exit 1
fi

cd - >/dev/null

echo "Installing update..."
cp -R "${source_dir}/." .
chmod +x "./${APP_NAME}" "./update-fcp-mod-manager.sh"

echo "Update complete. Start FCP Mod Manager normally."
