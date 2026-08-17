#!/usr/bin/env bash
#
# Bring up a PSU mock VISA device on a single ivi-cli process.
# Builds the v0.2.0 two-state FSM (off / on) directly from CLI verbs;
# the equivalent ready-to-drop TOML is in psu-bench.toml next to
# this script.
#
# Default: HiSLIP gateway on tcp/4880, sub-address `hislip0`.
#
# Requires: ivicli on PATH (dotnet tool install -g ivi-cli >= 0.2.0,
# or the GitHub Releases self-contained binary on PATH).
#
# Usage:
#   ./setup.sh                          # default: hislip-psu / 4880
#   PROTO=socket PORT=5025 ./setup.sh   # raw socket gateway
#   PORT=4881 ./setup.sh                # custom hislip port
#
# Idempotent: re-runs are safe; existing artifacts are kept.

set -euo pipefail

SCENARIO="${SCENARIO:-psu-bench}"
PROTO="${PROTO:-hislip}"
PORT="${PORT:-4880}"
SUBADDR="${SUBADDR:-hislip0}"     # only meaningful for hislip
SERVER="${SERVER:-${PROTO}-psu}"
DEVICE="${DEVICE:-psu_mock}"

echo "==> using scenario=$SCENARIO  proto=$PROTO  port=$PORT  device=$DEVICE  server=$SERVER"

# 1) Scenario + scenes — define the PSU's FSM (off/on).
# `--initial off` makes the FSM start in the `off` scene, so the
# synthetic `default` scene v0.2.0 used to auto-create is skipped.
ivicli mock scenario create "$SCENARIO" --initial off || true
ivicli mock scenario scene add "$SCENARIO" on        || true

# Static metadata duplicated across both scenes (v0.2.0 limitation —
# scenes do not share rules; key-value variable state is a follow-up).
for SCENE in off on; do
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match '*IDN?'      --respond 'IVICLI-MOCK,PSU,SN0001,1.0.0' || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match '*RST'       --ack --transition-to off               || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match '*OPC?'      --respond '1'                           || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match 'VOLT 5.0'   --ack                                   || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match 'VOLT?'      --respond '5.000'                       || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match 'CURR 1.0'   --ack                                   || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match 'CURR?'      --respond '1.000'                       || true
  ivicli mock scenario rule add "$SCENARIO" --in "$SCENE" --match 'SYST:ERR?'  --respond '0,"No error"'                || true
done

# off-specific: OUTP? is 0; OUTP ON moves to `on`.
ivicli mock scenario rule add "$SCENARIO" --in off --match 'OUTP?'      --respond '0'                 || true
ivicli mock scenario rule add "$SCENARIO" --in off --match 'OUTP ON'    --ack --transition-to on      || true
ivicli mock scenario rule add "$SCENARIO" --in off --match 'OUTP OFF'   --ack                          || true
ivicli mock scenario rule add "$SCENARIO" --in off --match 'MEAS:VOLT?' --respond '0.001'              || true
ivicli mock scenario rule add "$SCENARIO" --in off --match 'MEAS:CURR?' --respond '0.000'              || true

# on-specific: OUTP? is 1; OUTP OFF moves back to `off`.
ivicli mock scenario rule add "$SCENARIO" --in on --match 'OUTP?'      --respond '1'                 || true
ivicli mock scenario rule add "$SCENARIO" --in on --match 'OUTP OFF'   --ack --transition-to off     || true
ivicli mock scenario rule add "$SCENARIO" --in on --match 'OUTP ON'    --ack                          || true
ivicli mock scenario rule add "$SCENARIO" --in on --match 'MEAS:VOLT?' --respond '4.998'              || true
ivicli mock scenario rule add "$SCENARIO" --in on --match 'MEAS:CURR?' --respond '0.823'              || true

# 2) Logical device alias the gateway will route to.
ivicli visa add "$DEVICE" 'TCPIP0::127.0.0.1::INSTR' || true

# 3) Bind the scenario to the device; the alias answers from the scenario from here on.
ivicli mock scenario activate "$SCENARIO" --for "$DEVICE"

# 4) Gateway server.
ivicli server add "$SERVER" --type "$PROTO" --port "$PORT" || true

# 5) Route binding.
if [ "$PROTO" = "hislip" ]; then
  ivicli server route add "$SERVER" "$SUBADDR" "$DEVICE" || true
else
  # SOCKET has no sub-address; the route endpoint is the port itself.
  ivicli server route add "$SERVER" "$PORT" "$DEVICE" || true
fi

# 6) Start. Since v0.1.3, the gateway honours the active scenario at
# backend-dispatch time, so IVICLI_MOCK_ONLY=1 is not required on the
# host CLI path.
ivicli server start "$SERVER"

echo
echo "==> mock PSU is live (state: off)."
if [ "$PROTO" = "hislip" ]; then
  echo "    Resource: TCPIP::localhost::$SUBADDR::INSTR  (HiSLIP, port $PORT)"
else
  echo "    Resource: TCPIP::localhost::$PORT::SOCKET    (raw socket)"
fi
echo
echo "Try it:"
if [ "$PROTO" = "hislip" ]; then
  echo "    ivicli visa add tester 'TCPIP::localhost::$SUBADDR::INSTR'"
else
  echo "    ivicli visa add tester 'TCPIP::localhost::$PORT::SOCKET'"
fi
echo "    ivicli visa query tester 'OUTP?'    # -> 0"
echo "    ivicli visa write tester 'OUTP ON'"
echo "    ivicli visa query tester 'OUTP?'    # -> 1  (state switched to on)"
echo "    ivicli visa query tester 'MEAS:VOLT?'  # -> 4.998 (was 0.001 when off)"
echo "    ivicli visa write tester 'OUTP OFF'"
echo "    ivicli visa query tester 'OUTP?'    # -> 0  (back to off)"
