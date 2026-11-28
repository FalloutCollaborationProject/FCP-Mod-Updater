#!/usr/bin/env bash
# Posts a release announcement to Discord: the changelog notes for one version (changelog-notes.sh),
# shortened to about 1500 characters at a line break, with a link to the GitHub release.
#   discord-notify.sh <version> <release-url>
# DISCORD_WEBHOOK_URL  webhook to post to; empty = skip with a notice (exit 0).
# DISCORD_DRY_RUN=1    print the JSON payload instead of posting it.
set -euo pipefail

VERSION="${1:?usage: discord-notify.sh <version> <release-url>}"
URL="${2:?usage: discord-notify.sh <version> <release-url>}"
LIMIT=1500
DIR="$(dirname "$0")"

if [[ -z "${DISCORD_WEBHOOK_URL:-}" && "${DISCORD_DRY_RUN:-}" != 1 ]]; then
    echo "::notice::DISCORD_WEBHOOK_URL is not set; skipping the Discord announcement."
    exit 0
fi

# Headings don't render in embeds: "### Added" -> "**Added**". Squeeze blank lines, then cut at a line break.
NOTES="$(bash "$DIR/changelog-notes.sh" "$VERSION" | awk -v limit="$LIMIT" '
    /^#+ / { sub(/^#+ +/, ""); $0 = "**" $0 "**" }
    /^[[:space:]]*$/ { if (blank || total == 0) next; blank = 1 }
    !/^[[:space:]]*$/ { blank = 0 }
    {
        if (total + length($0) + 1 > limit) { cut = 1; exit }
        out = out (total ? "\n" : "") $0
        total += length($0) + 1
    }
    END {
        sub(/\n+$/, "", out)
        printf "%s", out
        if (cut) printf "\n…"
    }
')"

if [[ "$NOTES" == *$'\n…' ]]; then
    NOTES+=$'\n\n'"**[Full release notes]($URL)**"
else
    NOTES+=$'\n\n'"**[Download]($URL)**"
fi

if [[ "$VERSION" == *-* ]]; then
    TITLE="Collaboration Project Updater v$VERSION (pre-release)"
    COLOR=15105570   # amber
else
    TITLE="Collaboration Project Updater v$VERSION"
    COLOR=3066993    # green
fi

PAYLOAD="$(jq -n --arg title "$TITLE" --arg url "$URL" --arg desc "$NOTES" --argjson color "$COLOR" '{
    username: "Collaboration Project Updater",
    avatar_url: "https://raw.githubusercontent.com/FalloutCollaborationProject/FCP-Mod-Updater/main/FCPModUpdater.Gui/Assets/collab_logo.png",
    allowed_mentions: { parse: [] },
    embeds: [{ title: $title, url: $url, description: $desc, color: $color }]
}')"

if [[ "${DISCORD_DRY_RUN:-}" == 1 ]]; then
    printf '%s\n' "$PAYLOAD"
    exit 0
fi

curl -fsS -H 'Content-Type: application/json' -d "$PAYLOAD" "$DISCORD_WEBHOOK_URL"
echo "Posted the Discord announcement for v$VERSION."
