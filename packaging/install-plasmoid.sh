#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -eq 0 ]]; then
    echo "error: run this as your normal user, not with sudo" >&2
    exit 1
fi

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE="$(dirname -- "$SCRIPT_DIR")/plasma/org.cpupowermon.widget"

if ! command -v kpackagetool6 >/dev/null 2>&1; then
    echo "error: kpackagetool6 not found" >&2
    exit 1
fi

if [[ ! -f $PACKAGE/metadata.json ]]; then
    echo "error: missing $PACKAGE/metadata.json" >&2
    exit 1
fi

if kpackagetool6 --type Plasma/Applet --show org.cpupowermon.widget >/dev/null 2>&1; then
    kpackagetool6 --type Plasma/Applet --upgrade "$PACKAGE"
else
    kpackagetool6 --type Plasma/Applet --install "$PACKAGE"
fi

echo "Installed applet org.cpupowermon.widget."
echo "Add 'CPU Power Monitor' in the panel/desktop editor."
echo "If it does not appear, restart plasmashell: systemctl --user restart plasma-plasmashell.service"
