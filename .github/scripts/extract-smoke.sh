#!/bin/sh
# Start-up of a published Windows ivicli writes nothing to disk. The
# single-file bundle extracts each native library it carries to a
# directory under %TEMP% on every start, and on Windows ivicli loads none,
# so a file in that directory is one the publish should have left out.
#
# Usage: extract-smoke.sh <ivicli.exe>
set -eu

bin="${1:?usage: extract-smoke.sh <ivicli.exe>}"

dir="$(mktemp -d)"
trap 'rm -rf "$dir"' EXIT

DOTNET_BUNDLE_EXTRACT_BASE_DIR="$dir" "$bin" --version >/dev/null

extracted="$(find "$dir" -type f)"
if [ -n "$extracted" ]; then
  echo "extract smoke FAILED: start-up extracted"
  echo "$extracted"
  exit 1
fi
echo "extract smoke passed"
