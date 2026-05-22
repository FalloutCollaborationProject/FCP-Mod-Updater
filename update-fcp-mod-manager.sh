#!/usr/bin/env bash
set -euo pipefail

APP_NAME="FCPModUpdater"
ARCHIVE="FCPModUpdater-linux-x64-selfcontained.tar.gz"
BASE_URL="https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases/latest/download"

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

echo "Downloading latest FCP Mod Manager release..."
curl -fsSL "${BASE_URL}/${ARCHIVE}" -o "${staging}/${ARCHIVE}"
curl -fsSL "${BASE_URL}/checksums.txt" -o "${staging}/checksums.txt"

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
