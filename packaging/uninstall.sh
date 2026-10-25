#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "error: run this script with sudo" >&2
    exit 1
fi

systemctl disable --now cpumond.service 2>/dev/null || true

rm -f /etc/systemd/system/cpumond.service
rm -f /etc/dbus-1/system.d/org.cpupowermon.CpuMon.conf
rm -f /etc/dbus-1/system-services/org.cpupowermon.CpuMon.service
rm -rf /usr/local/lib/cpupowermon
rm -f /usr/local/bin/cpumonctl

systemctl daemon-reload
if ! busctl call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ReloadConfig >/dev/null 2>&1; then
    systemctl reload dbus-broker.service >/dev/null 2>&1 \
        || echo "warning: D-Bus config reload failed; a reboot may be needed" >&2
fi

echo "Removed cpumond.service, D-Bus policy/activation files, and cpumonctl."
echo "Remove the applet as your normal user: ./packaging/uninstall-plasmoid.sh"
