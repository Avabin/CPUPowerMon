#!/usr/bin/env bash
#
# Non-root reconnect test for cpumond, using a private session dbus-daemon.
# Kills the bus, restarts it on the same socket, and verifies that cpumond
# reconnects, re-requests its name, and keeps serving the contract.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BIN="${CPUMOND_BIN:-$ROOT/artifacts/service/cpumond}"
NAME="${CPUMON_RECONNECT_NAME:-org.cpupowermon.CpuMon.ServiceReconnect}"
OBJECT="/org/cpupowermon/CpuMon"
IFACE="org.cpupowermon.CpuMon1"
DAEMON="/usr/bin/dbus-daemon"
[[ -x "$DAEMON" ]] || DAEMON="$(command -v dbus-daemon)"

if [[ ! -x "$BIN" ]]; then
  echo "FAIL: $BIN is missing or not executable; publish it first." >&2
  exit 2
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/cpumond-reconnect.XXXXXX")"
SOCKET="$WORK/bus"
ADDRESS="unix:path=$SOCKET"
DEVICE="$WORK/rapl"
LOG="$WORK/cpumond.log"
BUS_LOG="$WORK/bus.log"
SERVICE_PID=""
BUS_PID=""

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cleanup() {
  local status=$?
  trap - EXIT
  set +e
  if [[ -n "$SERVICE_PID" ]] && kill -0 "$SERVICE_PID" 2>/dev/null; then
    kill -TERM "$SERVICE_PID" 2>/dev/null
    wait "$SERVICE_PID" 2>/dev/null
  fi
  if [[ -n "$BUS_PID" ]] && kill -0 "$BUS_PID" 2>/dev/null; then
    kill -TERM "$BUS_PID" 2>/dev/null
    wait "$BUS_PID" 2>/dev/null
  fi
  if [[ $status -ne 0 ]]; then
    echo "--- cpumond log ---" >&2
    cat "$LOG" >&2 2>/dev/null || true
    echo "--- bus log ---" >&2
    cat "$BUS_LOG" >&2 2>/dev/null || true
  fi
  if [[ "${KEEP_E2E:-0}" == "1" ]]; then
    echo "kept test directory: $WORK"
  else
    rm -rf "$WORK"
  fi
  exit "$status"
}
trap cleanup EXIT

start_bus() {
  rm -f "$SOCKET"
  "$DAEMON" --session --address="$ADDRESS" --nofork --print-address=1 \
    >>"$BUS_LOG" 2>&1 &
  BUS_PID=$!
  for _ in $(seq 1 100); do
    if busctl --address="$ADDRESS" list >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$BUS_PID" 2>/dev/null; then
      return 1
    fi
    sleep 0.05
  done
  return 1
}

wait_for_name() {
  for _ in $(seq 1 240); do
    if busctl --address="$ADDRESS" list --no-legend 2>/dev/null |
        awk '{print $1}' | grep -Fxq "$NAME"; then
      return 0
    fi
    if ! kill -0 "$SERVICE_PID" 2>/dev/null; then
      return 1
    fi
    sleep 0.05
  done
  return 1
}

start_bus || fail "private dbus-daemon did not start"

mkdir -p "$DEVICE"
printf '1000000\n' >"$DEVICE/energy_uj"
printf '100000000\n' >"$DEVICE/max_energy_range_uj"
printf 'package-0\n' >"$DEVICE/name"

DBUS_SESSION_BUS_ADDRESS="$ADDRESS" "$BIN" --bus session --device "$DEVICE" \
  --name "$NAME" --interval-ms 200 --verbose >"$LOG" 2>&1 &
SERVICE_PID=$!

wait_for_name || fail "cpumond did not acquire $NAME on the private bus"
VERSION="$(busctl --address="$ADDRESS" get-property "$NAME" "$OBJECT" "$IFACE" Version)"
[[ "$VERSION" == 's "0.1.0"' ]] || fail "unexpected Version: $VERSION"
echo "PASS: first bus connection, Version=$VERSION"

kill -TERM "$BUS_PID"
wait "$BUS_PID" 2>/dev/null || true
BUS_PID=""
sleep 0.3
kill -0 "$SERVICE_PID" || fail "cpumond exited when its bus disappeared"
echo "PASS: cpumond survived bus loss"

start_bus || fail "private dbus-daemon did not restart"
wait_for_name || fail "cpumond did not re-request $NAME after bus restart"

VERSION="$(busctl --address="$ADDRESS" get-property "$NAME" "$OBJECT" "$IFACE" Version)"
AVAILABLE="$(busctl --address="$ADDRESS" get-property "$NAME" "$OBJECT" "$IFACE" Available)"
[[ "$VERSION" == 's "0.1.0"' ]] || fail "unexpected Version after reconnect: $VERSION"
[[ "$AVAILABLE" == 'b true' ]] || fail "expected Available=true after reconnect, got '$AVAILABLE'"

busctl --address="$ADDRESS" call "$NAME" "$OBJECT" "$IFACE" GetSample >/dev/null
echo "PASS: reconnected, re-requested name, and still serves GetSample"

kill -TERM "$SERVICE_PID"
for _ in $(seq 1 50); do
  kill -0 "$SERVICE_PID" 2>/dev/null || break
  sleep 0.1
done
kill -0 "$SERVICE_PID" 2>/dev/null && fail "cpumond did not exit on SIGTERM"
set +e
wait "$SERVICE_PID"
EXIT_CODE=$?
set -e
SERVICE_PID=""
[[ "$EXIT_CODE" -eq 0 ]] || fail "cpumond exited with status $EXIT_CODE on SIGTERM"
grep -q 'reconnecting' "$LOG" || fail "reconnect was not logged"
echo "PASS: clean SIGTERM after reconnect"
echo "RECONNECT PASS: $NAME"
