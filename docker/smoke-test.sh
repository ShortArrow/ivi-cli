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
    echo "smoke passed"
    ;;
  *)
    echo "smoke FAILED — unexpected response"
    docker logs "$name"
    exit 1
    ;;
esac

docker rm -f "$name"
