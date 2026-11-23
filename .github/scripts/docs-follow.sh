#!/usr/bin/env bash
# Fails a change whose documents did not follow it:
#   - a bilingual pair (PRD, README, CONTRIBUTING, conformance) changes on
#     one side only;
#   - a feat, fix, perf or breaking change to src/ leaves docs/CHANGELOG.md
#     alone;
#   - docs/cli-reference.md changes, or a test's pinned output loses a line,
#     without docs/PRD.md and docs/CHANGELOG.md changing;
#   - a command is added or removed without README.md changing.
# A commit line "Docs-Unaffected: <reason>" lifts every rule but the pairs.
#
# Usage:
#   docs-follow.sh <base> <head> [<title>]
#       Reads the change since the merge base of <base> and <head> from
#       git. <title> is the pull request title, the
#       subject of the squashed commit; without it every commit subject in
#       <base>..<head> is read for its type.
#   docs-follow.sh decide <files> <messages> <title> <old-ref> <new-ref> <pinned-diff>
#       Decides from files: the changed paths, the commit messages, the
#       command reference before and after, and the diff of the tests that
#       pin output.
set -euo pipefail

readonly reference=docs/cli-reference.md
readonly pinned_tests=':(glob)tests/**/*FormatterTests.cs'
readonly pairs="docs/PRD.md docs/PRD.jp.md
README.md docs/README.jp.md
docs/CONTRIBUTING.md docs/CONTRIBUTING.jp.md
docs/conformance.md docs/conformance.jp.md"
readonly needs_changelog='^((feat|fix|perf)(\([^)]*\))?!?|[a-z]+(\([^)]*\))?!): |^BREAKING[ -]CHANGE: '

decide() {
  local files_file="$1" messages_file="$2" title="$3" old_ref="$4" new_ref="$5" pinned="$6"
  local errors=0

  changed() { grep -qx -- "$1" "$files_file"; }
  error() {
    echo "::error::$1"
    errors=$((errors + 1))
  }

  while read -r en jp; do
    if changed "$en" && ! changed "$jp"; then
      error "$en changed but $jp did not; change both in the same pull request."
    elif changed "$jp" && ! changed "$en"; then
      error "$jp changed but $en did not; change both in the same pull request."
    fi
  done <<<"$pairs"

  local reasons
  reasons="$(grep -E '^Docs-Unaffected:[[:space:]]*[^[:space:]]' "$messages_file" || true)"
  if [ -n "$reasons" ]; then
    echo "Documents not required:"
    echo "$reasons" | sed 's/^Docs-Unaffected:[[:space:]]*/  - /'
    [ "$errors" -eq 0 ] || return 1
    return 0
  fi

  local types
  if [ -n "$title" ]; then types="$title"; else types="$(cat "$messages_file")"; fi
  if grep -qE '^src/' "$files_file" && grep -qE "$needs_changelog" <<<"$types" \
    && ! changed docs/CHANGELOG.md; then
    error "This change to src/ is a feat, fix, perf or breaking change, so docs/CHANGELOG.md needs an entry under [Unreleased]."
  fi

  if [ -s "$old_ref" ] && ! cmp -s "$old_ref" "$new_ref"; then
    changed docs/PRD.md || error "$reference changed, so the commands it lists changed; update docs/PRD.md and docs/PRD.jp.md."
    changed docs/CHANGELOG.md || error "$reference changed; add a docs/CHANGELOG.md entry for the command change."
    if ! diff -q <(grep '^## `' "$old_ref" | sort) <(grep '^## `' "$new_ref" | sort) >/dev/null; then
      changed README.md || error "A command was added or removed; update the command map in README.md and docs/README.jp.md."
    fi
  fi

  if grep -qE '^-[^-].*"' "$pinned"; then
    changed docs/PRD.md || error "A test that pins command output changed an existing expectation; update the output contract in docs/PRD.md and docs/PRD.jp.md."
    changed docs/CHANGELOG.md || error "A test that pins command output changed an existing expectation; add a docs/CHANGELOG.md entry."
  fi

  if [ "$errors" -ne 0 ]; then
    echo "If no document describes this change, add a line 'Docs-Unaffected: <reason>' to a commit message."
    return 1
  fi
  echo "docs-follow: the documents follow the change"
}

if [ "${1:-}" = decide ]; then
  shift
  decide "$@"
  exit
fi

head="${2:?usage: docs-follow.sh <base> <head> [<title>]}"
base="$(git merge-base "${1:?usage: docs-follow.sh <base> <head> [<title>]}" "$head")"
title="${3:-}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
git diff --name-only "$base" "$head" >"$work/files"
git log --format='%B' "$base..$head" >"$work/messages"
git show "$base:$reference" >"$work/old" 2>/dev/null || : >"$work/old"
git show "$head:$reference" >"$work/new" 2>/dev/null || : >"$work/new"
git diff "$base" "$head" -- "$pinned_tests" >"$work/pinned"
decide "$work/files" "$work/messages" "$title" "$work/old" "$work/new" "$work/pinned"
