#!/usr/bin/env bash
#
# Bring up a PSU mock VISA device on a single ivi-cli process.
# Builds the v0.2.0 two-state FSM (off / on) directly from CLI verbs;
# the equivalent ready-to-drop TOML is in psu-bench.toml next to
# this script.
#
# Default: HiSLIP gateway on tcp/4880, sub-address `hislip0`. The
# server runs in the foreground until Ctrl+C; `ivicli mock unserve
# psu-mock` then removes the device and server it registered.
#
# Requires: ivicli on PATH (dotnet tool install -g ivi-cli >= 0.2.0,
# or the GitHub Releases self-contained binary on PATH).
#
# Usage:
#   ./setup.sh                          # default: hislip / 4880
#   PROTO=socket PORT=5025 ./setup.sh   # raw socket gateway
#   PORT=4881 ./setup.sh                # custom hislip port
#
# Idempotent: re-runs are safe; existing artifacts are kept.

set -euo pipefail

SCENARIO="${SCENARIO:-psu-bench}"
PROTO="${PROTO:-hislip}"
PORT="${PORT:-4880}"
NAME="${NAME:-psu-mock}"          # the device and the server `mock serve` registers

echo "==> using scenario=$SCENARIO  proto=$PROTO  port=$PORT  name=$NAME"

# 1) Scenario + scenes — define the PSU's FSM (off/on).
# `--initial off` makes the FSM start in the `off` scene, so the
# synthetic `default` scene v0.2.0 used to auto-create is skipped.
ivicli mock scenario create "$SCENARIO" --initial off || true
ivicli mock scene add "$SCENARIO" on        || true

# Static metadata duplicated across both scenes (v0.2.0 limitation —
# scenes do not share rules; key-value variable state is a follow-up).
for SCENE in off on; do
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match '*IDN?'      --respond 'IVICLI-MOCK,PSU,SN0001,1.0.0' || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match '*RST'       --ack --transition-to off               || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match '*OPC?'      --respond '1'                           || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match 'VOLT 5.0'   --ack                                   || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match 'VOLT?'      --respond '5.000'                       || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match 'CURR 1.0'   --ack                                   || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match 'CURR?'      --respond '1.000'                       || true
  ivicli mock rule add "$SCENARIO" --in "$SCENE" --match 'SYST:ERR?'  --respond '0,"No error"'                || true
done

# off-specific: OUTP? is 0; OUTP ON moves to `on`.
ivicli mock rule add "$SCENARIO" --in off --match 'OUTP?'      --respond '0'                 || true
ivicli mock rule add "$SCENARIO" --in off --match 'OUTP ON'    --ack --transition-to on      || true
ivicli mock rule add "$SCENARIO" --in off --match 'OUTP OFF'   --ack                          || true
ivicli mock rule add "$SCENARIO" --in off --match 'MEAS:VOLT?' --respond '0.001'              || true
ivicli mock rule add "$SCENARIO" --in off --match 'MEAS:CURR?' --respond '0.000'              || true

# on-specific: OUTP? is 1; OUTP OFF moves back to `off`.
ivicli mock rule add "$SCENARIO" --in on --match 'OUTP?'      --respond '1'                 || true
ivicli mock rule add "$SCENARIO" --in on --match 'OUTP OFF'   --ack --transition-to off     || true
ivicli mock rule add "$SCENARIO" --in on --match 'OUTP ON'    --ack                          || true
ivicli mock rule add "$SCENARIO" --in on --match 'MEAS:VOLT?' --respond '4.998'              || true
ivicli mock rule add "$SCENARIO" --in on --match 'MEAS:CURR?' --respond '0.823'              || true

if [ "$PROTO" = "hislip" ]; then
  RESOURCE="TCPIP::127.0.0.1::hislip0,$PORT::INSTR"
else
  RESOURCE="TCPIP::127.0.0.1::$PORT::SOCKET"
fi

echo
echo "Try it from another terminal once the server listens:"
echo "    ivicli visa add tester '$RESOURCE'"
echo "    ivicli visa query tester 'OUTP?'    # -> 0"
echo "    ivicli visa write tester 'OUTP ON'"
echo "    ivicli visa query tester 'OUTP?'    # -> 1  (state switched to on)"
echo "    ivicli visa query tester 'MEAS:VOLT?'  # -> 4.998 (was 0.001 when off)"
echo "    ivicli visa write tester 'OUTP OFF'"
echo "    ivicli visa query tester 'OUTP?'    # -> 0  (back to off)"
echo

# 2) Serve the scenario: registers the device and the server under $NAME
# and runs the server in the foreground.
exec ivicli mock serve "$SCENARIO" --type "$PROTO" --port "$PORT" --alias "$NAME"
