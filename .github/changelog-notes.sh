#!/usr/bin/env bash
# Prints the release notes for one version from CHANGELOG.md.
#   changelog-notes.sh <version> [changelog]     e.g. 1.0.0-rc.2 or 1.0.0
# A pre-release prints its own section. A stable release prints its own section followed by every
# section of its pre-releases (X.Y.Z-*), each under a "### X.Y.Z-label" heading with its subheadings
# moved down one level. No matching section: prints nothing and a warning, exit 0.
set -euo pipefail

VERSION="${1:?usage: changelog-notes.sh <version> [changelog]}"
CHANGELOG="${2:-$(dirname "$0")/../CHANGELOG.md}"

awk -v version="$VERSION" '
    function flush() {
        # Trim blank lines at both ends of the collected section body.
        while (n > 0 && body[n] ~ /^[[:space:]]*$/) n--
        start = 1
        while (start <= n && body[start] ~ /^[[:space:]]*$/) start++
        if (start > n) { n = 0; return }
        if (printed) print ""
        if (section != version) print "### " section "\n"
        for (i = start; i <= n; i++) print body[i]
        printed = 1
        n = 0
    }
    # Section ends at the next "## " heading or a "---" rule.
    /^## / || /^---[[:space:]]*$/ {
        if (in_section) flush()
        in_section = 0
        if (match($0, /^## \[[^]]+\]/)) {
            section = substr($0, 5, RLENGTH - 5)
            if (section == version) { in_section = 1; found = 1 }
            else if (stable && index(section, version "-") == 1) { in_section = 1; found = 1 }
        }
        next
    }
    in_section {
        line = $0
        if (section != version && line ~ /^#+ /) line = "#" line
        body[++n] = line
    }
    BEGIN { stable = (index(version, "-") == 0); n = 0 }
    END {
        if (in_section) flush()
        if (!found) {
            print "::warning::CHANGELOG.md has no section for " version > "/dev/stderr"
        }
    }
' "$CHANGELOG"
