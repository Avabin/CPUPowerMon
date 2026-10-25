#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -ne 0 ]]; then
    echo "error: run this script with sudo" >&2
    exit 1
fi

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname -- "$SCRIPT_DIR")"
SERVICE_BIN="${1:-${CPUMON_SERVICE_BIN:-$REPO_ROOT/artifacts/service/cpumond}}"
CLIENT_BIN="${CPUMON_CLIENT_BIN:-$REPO_ROOT/artifacts/client/cpumonctl}"
SYSTEMD_UNIT="$REPO_ROOT/systemd/cpumond.service"
DBUS_POLICY="$REPO_ROOT/dbus/org.cpupowermon.CpuMon.conf"

require_file() {
    if [[ ! -f $1 ]]; then
        echo "error: missing $1 (run packaging/publish.sh first)" >&2
        exit 1
    fi
}

require_file "$SERVICE_BIN"
require_file "$CLIENT_BIN"
require_file "$SYSTEMD_UNIT"
require_file "$DBUS_POLICY"

install -Dm755 "$SERVICE_BIN" /usr/local/lib/cpupowermon/cpumond
install -Dm755 "$CLIENT_BIN" /usr/local/bin/cpumonctl
install -Dm644 "$SYSTEMD_UNIT" /etc/systemd/system/cpumond.service
install -Dm644 "$DBUS_POLICY" /etc/dbus-1/system.d/org.cpupowermon.CpuMon.conf

shopt -s nullglob
for activation in "$REPO_ROOT"/dbus/*.service; do
    install -Dm644 "$activation" "/etc/dbus-1/system-services/$(basename -- "$activation")"
done
shopt -u nullglob

systemctl daemon-reload

if ! busctl call org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus ReloadConfig >/dev/null 2>&1; then
    systemctl reload dbus-broker.service >/dev/null 2>&1 \
        || echo "warning: D-Bus config reload failed; a reboot may be needed for the policy" >&2
fi

systemctl enable cpumond.service
systemctl restart cpumond.service

echo
systemctl --no-pager --full --lines=10 status cpumond.service || true
echo
echo "Installed. Try: cpumonctl; cpumonctl --json"
echo "Install the applet as your normal user: ./packaging/install-plasmoid.sh"
