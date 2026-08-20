#!/usr/bin/env bash
# Mock-VISA container entrypoint (ADR 0018 §6).
#
# Starts three `ivicli server start` processes — a HiSlip gateway on
# 4880, a SOCKET gateway on 5025, and a VXI-11 gateway on 111 (TCP core
# + portmap on one port, plus the UDP portmapper responder), per the
# pre-baked config.toml.
# A SIGTERM / SIGINT trap forwards the signal to both child PIDs so
# `docker stop` shuts down cleanly inside the 10-second grace period.
set -euo pipefail

PIDS=()

terminate() {
    # Forward to children; ignore failures from already-dead PIDs.
    for pid in "${PIDS[@]}"; do
        kill -TERM "$pid" 2>/dev/null || true
    done
    wait
    exit 0
}

trap terminate TERM INT

ivicli server start hislip-mock &
PIDS+=($!)

ivicli server start socket-mock &
PIDS+=($!)

ivicli server start vxi11-mock &
PIDS+=($!)

# Block until any child exits (success or failure), then propagate to
# the other and exit with the offending status. `wait -n` returns the
# first child's exit status.
set +e
wait -n
status=$?
terminate
exit "$status"
