#!/usr/bin/env bash
#
# Non-root session-bus end-to-end test for cpumond.
#
# It starts the published self-contained binary against a temporary fake RAPL
# device, exercises the full frozen D-Bus contract with busctl, verifies a known
# 5 W counter rate, verifies missing-device -> recovery and runtime failure ->
# recovery, checks the PowerChanged signal, and verifies clean SIGTERM exit.
#
# The service name is intentionally not the one owned by the real service. It
# can be overridden with CPUMON_E2E_NAME to run several agents in parallel.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BIN="${CPUMOND_BIN:-$ROOT/artifacts/service/cpumond}"
NAME="${CPUMON_E2E_NAME:-org.cpupowermon.CpuMon.ServiceE2E}"
OBJECT="/org/cpupowermon/CpuMon"
IFACE="org.cpupowermon.CpuMon1"
INTERVAL_MS=200
RATE_UJ_PER_SECOND=5000000

if [[ ! -x "$BIN" ]]; then
  echo "FAIL: $BIN is missing or not executable." >&2
  echo "Publish it first:" >&2
  echo "  dotnet publish src/CpuMon.Service/CpuMon.Service.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/service" >&2
  exit 2
fi

if [[ -z "${DBUS_SESSION_BUS_ADDRESS:-}" ]]; then
  echo "FAIL: DBUS_SESSION_BUS_ADDRESS is not set; a user session bus is required." >&2
  exit 2
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/cpumond-e2e.XXXXXX")"
DEVICE="$WORK/rapl"
LOG="$WORK/cpumond.log"
WRITER_LOG="$WORK/writer.log"
SIGNAL_LOG="$WORK/signals.log"
SERVICE_PID=""
WRITER_PID=""
MONITOR_PID=""

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cleanup() {
  local status=$?
  trap - EXIT
  set +e

  if [[ -n "$MONITOR_PID" ]] && kill -0 "$MONITOR_PID" 2>/dev/null; then
    kill "$MONITOR_PID" 2>/dev/null
    wait "$MONITOR_PID" 2>/dev/null
  fi
  if [[ -n "$WRITER_PID" ]] && kill -0 "$WRITER_PID" 2>/dev/null; then
    kill "$WRITER_PID" 2>/dev/null
    wait "$WRITER_PID" 2>/dev/null
  fi
  if [[ -n "$SERVICE_PID" ]] && kill -0 "$SERVICE_PID" 2>/dev/null; then
    kill -TERM "$SERVICE_PID" 2>/dev/null
    for _ in $(seq 1 50); do
      kill -0 "$SERVICE_PID" 2>/dev/null || break
      sleep 0.1
    done
    kill -KILL "$SERVICE_PID" 2>/dev/null
    wait "$SERVICE_PID" 2>/dev/null
  fi

  if [[ $status -ne 0 ]]; then
    echo "--- cpumond log ---" >&2
    cat "$LOG" >&2 2>/dev/null || true
    if [[ -s "$SIGNAL_LOG" ]]; then
      echo "--- signal monitor log ---" >&2
      cat "$SIGNAL_LOG" >&2 || true
    fi
  fi

  if [[ "${KEEP_E2E:-0}" == "1" ]]; then
    echo "kept test directory: $WORK"
  else
    rm -rf "$WORK"
  fi
  exit "$status"
}
trap cleanup EXIT

get_prop() {
  busctl --user get-property "$NAME" "$OBJECT" "$IFACE" "$1"
}

get_scalar() {
  get_prop "$1" | awk '{print $2}'
}

get_string() {
  local raw
  raw="$(get_prop "$1")"
  raw="${raw#s }"
  raw="${raw#\"}"
  raw="${raw%\"}"
  printf '%s' "$raw"
}

assert_scalar() {
  local actual
  actual="$(get_scalar "$1")"
  [[ "$actual" == "$2" ]] || fail "$1 expected '$2', got '$actual'"
}

assert_string() {
  local actual
  actual="$(get_string "$1")"
  [[ "$actual" == "$2" ]] || fail "$1 expected '$2', got '$actual'"
}

wait_for_name() {
  for _ in $(seq 1 200); do
    if busctl --user list --no-legend 2>/dev/null | awk '{print $1}' | grep -Fxq "$NAME"; then
      return 0
    fi
    if ! kill -0 "$SERVICE_PID" 2>/dev/null; then
      echo "cpumond exited before acquiring the name" >&2
      return 1
    fi
    sleep 0.05
  done
  return 1
}

wait_for_available() {
  local expected="$1"
  for _ in $(seq 1 120); do
    if [[ "$(get_prop Available 2>/dev/null || true)" == "b $expected" ]]; then
      return 0
    fi
    sleep 0.05
  done
  return 1
}

wait_for_last_error() {
  for _ in $(seq 1 120); do
    if [[ "$(get_prop LastError 2>/dev/null || true)" != 's ""' ]]; then
      return 0
    fi
    sleep 0.05
  done
  return 1
}

wait_for_watts() {
  local low="$1"
  local high="$2"
  for _ in $(seq 1 80); do
    local raw watts
    raw="$(get_prop PowerWatts 2>/dev/null || true)"
    watts="${raw#d }"
    if [[ -n "$watts" ]] && awk -v w="$watts" -v lo="$low" -v hi="$high" \
        'BEGIN { exit !(w >= lo && w <= hi) }'; then
      return 0
    fi
    sleep 0.1
  done
  return 1
}

assert_sample_tuple() {
  local sample sig energy watts timestamp available
  sample="$(busctl --user call "$NAME" "$OBJECT" "$IFACE" GetSample)"
  read -r sig energy watts timestamp available <<<"$sample"

  [[ "$sig" == "(tdtb)" ]] || fail "GetSample signature expected (tdtb), got '$sig'"
  [[ "$available" == "true" ]] || fail "GetSample available expected true, got '$available'"
  [[ "$energy" =~ ^[0-9]+$ ]] || fail "GetSample energy is not an unsigned integer: '$energy'"
  [[ "$timestamp" =~ ^[0-9]+$ ]] || fail "GetSample timestamp is not an unsigned integer: '$timestamp'"
  awk -v w="$watts" -v lo=4.5 -v hi=5.5 \
    'BEGIN { exit !(w >= lo && w <= hi) }' ||
    fail "GetSample watts expected ~5 W, got '$watts'"

  printf 'GetSample: %s\n' "$sample"
}

# ---------------------------------------------------------------------------
# 1. Start with a missing device. The service must stay up and report
#    Available=false with a non-empty LastError.
# ---------------------------------------------------------------------------
"$BIN" --bus session --device "$DEVICE" --name "$NAME" \
  --interval-ms "$INTERVAL_MS" --verbose >"$LOG" 2>&1 &
SERVICE_PID=$!

wait_for_name || fail "cpumond did not acquire $NAME"
wait_for_available false || fail "service did not report Available=false for a missing device"
wait_for_last_error || fail "service did not report LastError for a missing device"

assert_scalar PowerWatts 0
assert_scalar EnergyUj 0
assert_scalar MaxEnergyRangeUj 0
assert_scalar SampleIntervalMs "$INTERVAL_MS"
assert_string PackageName ""
assert_string Version "0.1.0"
echo "PASS: missing device -> Available=false, LastError='$(get_string LastError)'"

# ---------------------------------------------------------------------------
# 2. Introspection exposes the frozen contract.
# ---------------------------------------------------------------------------
INTROSPECTION="$(busctl --user introspect "$NAME" "$OBJECT")"
for token in "$IFACE" "GetPowerWatts" "GetSample" "Refresh" \
             "PowerWatts" "EnergyUj" "MaxEnergyRangeUj" "SampleIntervalMs" \
             "PackageName" "Available" "LastError" "Version" "PowerChanged"; do
  if ! grep -Fq "$token" <<<"$INTROSPECTION"; then
    fail "introspection is missing '$token'"
  fi
done
echo "PASS: introspection contains the full interface, methods, properties, and signal"

# ---------------------------------------------------------------------------
# 3. Create the fake device and feed a known +5,000,000 uJ/s (5 W) rate.
# ---------------------------------------------------------------------------
mkdir -p "$DEVICE"
printf '1000000\n' >"$DEVICE/energy_uj"
printf '100000000\n' >"$DEVICE/max_energy_range_uj"
printf 'package-0\n' >"$DEVICE/name"

cat >"$WORK/writer.py" <<'PY'
import os
import sys
import time

path = sys.argv[1]
base = int(sys.argv[2])
rate = int(sys.argv[3])
start = time.monotonic()
while True:
    value = base + int((time.monotonic() - start) * rate)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="ascii") as handle:
        handle.write(f"{value}\n")
    os.replace(tmp, path)
    time.sleep(0.02)
PY
python3 "$WORK/writer.py" "$DEVICE/energy_uj" 1000000 "$RATE_UJ_PER_SECOND" \
  >"$WRITER_LOG" 2>&1 &
WRITER_PID=$!

wait_for_available true || fail "service did not recover after the fake device appeared"
assert_scalar MaxEnergyRangeUj 100000000
assert_string PackageName package-0
assert_string LastError ""
wait_for_watts 4.5 5.5 || fail "PowerWatts did not settle near 5 W (got '$(get_scalar PowerWatts)')"

POWER="$(get_scalar PowerWatts)"
assert_sample_tuple
REFRESH_SAMPLE="$(busctl --user call "$NAME" "$OBJECT" "$IFACE" GetPowerWatts)"
echo "PASS: fake device recovery, PackageName=package-0, PowerWatts=$POWER, $REFRESH_SAMPLE"

# ---------------------------------------------------------------------------
# 4. Refresh takes a sample immediately and PowerChanged is emitted.
# ---------------------------------------------------------------------------
timeout 4s busctl --user monitor --json=short "$NAME" >"$SIGNAL_LOG" 2>&1 &
MONITOR_PID=$!
sleep 0.5

busctl --user call "$NAME" "$OBJECT" "$IFACE" Refresh >/dev/null
wait_for_watts 4.5 5.5 || fail "PowerWatts was not near 5 W after Refresh"
assert_sample_tuple

for _ in $(seq 1 30); do
  grep -q 'PowerChanged' "$SIGNAL_LOG" && break
  sleep 0.1
done
kill "$MONITOR_PID" 2>/dev/null || true
wait "$MONITOR_PID" 2>/dev/null || true
MONITOR_PID=""
grep -q 'PowerChanged' "$SIGNAL_LOG" || fail "PowerChanged signal was not observed"
echo "PASS: Refresh produced a sample and PowerChanged was observed"

# ---------------------------------------------------------------------------
# 5. Runtime read failure is reported, and the service recovers in place.
# ---------------------------------------------------------------------------
kill "$WRITER_PID" 2>/dev/null || true
wait "$WRITER_PID" 2>/dev/null || true
WRITER_PID=""

CURRENT_ENERGY="$(cat "$DEVICE/energy_uj")"
rm "$DEVICE/energy_uj"
wait_for_available false || fail "service did not report Available=false after energy_uj was removed"
wait_for_last_error || fail "service did not report LastError after energy_uj was removed"
assert_scalar PowerWatts 0
assert_scalar EnergyUj 0
kill -0 "$SERVICE_PID" || fail "service exited on a failed read"
echo "PASS: runtime failed read -> Available=false, service still running"

printf '%s\n' "$CURRENT_ENERGY" >"$DEVICE/energy_uj"
wait_for_available true || fail "service did not recover after energy_uj was restored"
assert_scalar EnergyUj "$CURRENT_ENERGY"
echo "PASS: runtime recovery -> Available=true without restart"

# ---------------------------------------------------------------------------
# 6. Clean SIGTERM shutdown.
# ---------------------------------------------------------------------------
kill -TERM "$SERVICE_PID"
for _ in $(seq 1 50); do
  kill -0 "$SERVICE_PID" 2>/dev/null || break
  sleep 0.1
done
if kill -0 "$SERVICE_PID" 2>/dev/null; then
  fail "cpumond did not exit within 5 s of SIGTERM"
fi

set +e
wait "$SERVICE_PID"
EXIT_CODE=$?
set -e
SERVICE_PID=""
[[ "$EXIT_CODE" -eq 0 ]] || fail "cpumond exited with status $EXIT_CODE on SIGTERM"
grep -q '^cpumond: stopped\.$' "$LOG" || fail "cpumond did not log a clean stop"
echo "PASS: SIGTERM exited cleanly (status 0)"
echo "E2E PASS: $NAME on the session bus"
