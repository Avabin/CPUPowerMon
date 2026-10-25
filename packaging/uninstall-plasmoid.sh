#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -eq 0 ]]; then
    echo "error: run this as your normal user, not with sudo" >&2
    exit 1
fi

if kpackagetool6 --type Plasma/Applet --remove org.cpupowermon.widget; then
    echo "Removed applet org.cpupowermon.widget."
    echo "Restart plasmashell to unload it: systemctl --user restart plasma-plasmashell.service"
else
    echo "Applet org.cpupowermon.widget is not installed." >&2
fi
