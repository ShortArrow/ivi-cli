#!/usr/bin/env bash
#
# Bring up a PSU mock VISA device on a single ivi-cli process.
# Default: HiSLIP gateway on tcp/4880, sub-address `hislip0`.
#
# Requires: ivicli on PATH (dotnet tool install -g ivi-cli, or
# the GitHub Releases self-contained binary on PATH).
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
DEVICE="${DEVICE:-psu-mock}"

echo "==> using scenario=$SCENARIO  proto=$PROTO  port=$PORT  device=$DEVICE  server=$SERVER"

# 1) Scenario + scenes — define the PSU's SCPI conversation.
ivicli mock scenario create "$SCENARIO" || true
ivicli mock scene add "$SCENARIO" --match '*IDN?'      --respond 'IVICLI-MOCK,PSU,SN0001,1.0.0' || true
ivicli mock scene add "$SCENARIO" --match '*RST'       --ack                                   || true
ivicli mock scene add "$SCENARIO" --match '*OPC?'      --respond '1'                           || true
ivicli mock scene add "$SCENARIO" --match 'OUTP ON'    --ack                                   || true
ivicli mock scene add "$SCENARIO" --match 'OUTP OFF'   --ack                                   || true
ivicli mock scene add "$SCENARIO" --match 'OUTP?'      --respond '1'                           || true
ivicli mock scene add "$SCENARIO" --match 'VOLT 5.0'   --ack                                   || true
ivicli mock scene add "$SCENARIO" --match 'VOLT?'      --respond '5.000'                       || true
ivicli mock scene add "$SCENARIO" --match 'CURR 1.0'   --ack                                   || true
ivicli mock scene add "$SCENARIO" --match 'CURR?'      --respond '1.000'                       || true
ivicli mock scene add "$SCENARIO" --match 'MEAS:VOLT?' --respond '4.998'                       || true
ivicli mock scene add "$SCENARIO" --match 'MEAS:CURR?' --respond '0.823'                       || true
ivicli mock scene add "$SCENARIO" --match 'SYST:ERR?'  --respond '0,"No error"'                || true

# 2) Activate so backend FakeBackend serves these responses.
ivicli mock scenario activate "$SCENARIO"

# 3) Logical device alias the gateway will route to.
ivicli visa add "$DEVICE" 'TCPIP0::127.0.0.1::INSTR' || true

# 4) Gateway server.
ivicli server add "$SERVER" --type "$PROTO" --port "$PORT" || true

# 5) Route binding.
if [ "$PROTO" = "hislip" ]; then
  ivicli server route add "$SERVER" "$SUBADDR" "$DEVICE" || true
else
  # SOCKET has no sub-address; the route endpoint is the port itself.
  ivicli server route add "$SERVER" "$PORT" "$DEVICE" || true
fi

# 6) Start.
ivicli server start "$SERVER"

echo
echo "==> mock PSU is live."
if [ "$PROTO" = "hislip" ]; then
  echo "    Resource: TCPIP::localhost::$SUBADDR::INSTR  (HiSLIP, port $PORT)"
  echo
  echo "Try it:"
  echo "    ivicli visa add tester TCPIP::localhost::$SUBADDR::INSTR"
else
  echo "    Resource: TCPIP::localhost::$PORT::SOCKET    (raw socket)"
  echo
  echo "Try it:"
  echo "    ivicli visa add tester TCPIP::localhost::$PORT::SOCKET"
fi
echo "    ivicli visa query tester '*IDN?'"
echo "    # → IVICLI-MOCK,PSU,SN0001,1.0.0"
