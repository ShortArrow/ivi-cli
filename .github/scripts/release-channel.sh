#!/usr/bin/env bash
# Prints the release channel of a version tag (ADR 0022):
#   prerelease   the version has a hyphen suffix (v0.4.0-beta.1)
#   latest       the highest final version, counting this one
#   maintenance  a final version below an already-released final version,
#                cut on a maintenance branch; it moves its own vX.Y tags
#                but never `latest`, `aot` or the GitHub "Latest" mark
#
# Usage: release-channel.sh <tag> [tags-file]
# Without a tags file the tags are read from `origin`.
set -euo pipefail
tag="$1"
case "$tag" in
  *-*) echo prerelease; exit 0 ;;
esac
if [ "$#" -ge 2 ]; then
  tags="$(cat "$2")"
else
  tags="$(git ls-remote --tags --refs origin 'v*' | sed 's#.*refs/tags/##')"
fi
highest="$(printf '%s\n%s\n' "$tags" "$tag" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -1)"
if [ "$tag" = "$highest" ]; then
  echo latest
else
  echo maintenance
fi
