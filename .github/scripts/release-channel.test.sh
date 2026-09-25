#!/usr/bin/env bash
# Checks release-channel.sh against ADR 0022: a hyphenated version is a
# pre-release; the highest final version is the latest; any other final
# version is a maintenance release that must not take `latest` back.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tags="$(mktemp)"
trap 'rm -f "$tags"' EXIT
printf '%s\n' v0.3.1 v0.3.2 v0.3.2-beta.1 v0.4.0-beta.1 v0.4.0 v0.10.0-beta.1 >"$tags"

failures=0
expect() {
  local tag="$1" want="$2" got
  got="$("$here/release-channel.sh" "$tag" "$tags")"
  if [ "$got" != "$want" ]; then
    echo "FAIL: $tag -> $got, want $want"
    failures=$((failures + 1))
  fi
}

expect v0.4.0-beta.1 prerelease
expect v0.10.0-beta.1 prerelease
expect v0.4.0 latest
expect v0.3.2 maintenance
expect v0.3.3 maintenance
expect v0.4.1 latest
expect v0.10.0 latest

if [ "$failures" -ne 0 ]; then
  echo "$failures case(s) failed"
  exit 1
fi
echo "release-channel: all cases pass"
