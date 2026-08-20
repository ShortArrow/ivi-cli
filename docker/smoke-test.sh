#!/bin/sh
# HEALTHCHECK + SCPI round-trip smoke against a locally built
# ivi-cli-mock image. Shared by pr-docker-smoke.yml and both
# release.yml container smoke gates (amd64 + arm64).
#
# Usage: smoke-test.sh <image-tag>
set -eu

image="${1:?usage: smoke-test.sh <image-tag>}"
name="mock-smoke"

docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --name "$name" "$image"

for i in $(seq 1 30); do
  status="$(docker inspect --format '{{.State.Health.Status}}' "$name")"
  if [ "$status" = "healthy" ]; then
    echo "container healthy after ${i}s"
    break
  fi
  sleep 1
done
if [ "$(docker inspect --format '{{.State.Health.Status}}' "$name")" != "healthy" ]; then
  echo "container failed to become healthy"
  docker logs "$name"
  exit 1
fi

# SCPI round-trip over the SOCKET gateway (no client framing
# required). Expect the baked default IDN response.
response="$(docker exec "$name" bash -c 'echo "*IDN?" | nc -w 2 127.0.0.1 5025')"
echo "got: $response"
case "$response" in
  *IVICLI-MOCK*)
    echo "socket smoke passed"
    ;;
  *)
    echo "socket smoke FAILED — unexpected response"
    docker logs "$name"
    exit 1
    ;;
esac

# SCPI round-trip over the VXI-11 gateway, driven by ivicli's own
# VXI-11 client backend: UDP GETPORT to 127.0.0.1:111 resolves the
# core port, then create_link + device_write/read. IVICLI_MOCK_ONLY
# must be off in the exec'd process or the client would answer from
# its own in-process fake instead of crossing the wire.
docker exec -e IVICLI_MOCK_ONLY=0 "$name" ivicli visa add vxi-probe 'TCPIP0::127.0.0.1::inst0::INSTR'
vxi_response="$(docker exec -e IVICLI_MOCK_ONLY=0 "$name" ivicli visa query vxi-probe '*IDN?')"
echo "got (vxi11): $vxi_response"
case "$vxi_response" in
  *IVICLI-MOCK*)
    echo "vxi11 smoke passed"
    ;;
  *)
    echo "vxi11 smoke FAILED — unexpected response"
    docker logs "$name"
    exit 1
    ;;
esac

docker rm -f "$name"
