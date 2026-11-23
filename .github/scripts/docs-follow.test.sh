#!/usr/bin/env bash
# Checks docs-follow.sh's decision against its rules: a bilingual pair
# changes together; a feat, fix, perf or breaking change to src/ updates the
# CHANGELOG; a change to the command reference or to a pinned output updates
# the PRD and the CHANGELOG; a command added or removed updates the README;
# a Docs-Unaffected trailer lifts every rule but the bilingual pairs.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

reference_v1='# Command reference

## `ivicli`

## `ivicli visa`
'
reference_v2_added="$reference_v1"'
## `ivicli visa wipe`
'
reference_v2_reworded='# Command reference

## `ivicli`

Reworded.

## `ivicli visa`
'

failures=0
# expect <case name> <pass|fail> <title> <changed files, one per line>
#        [messages] [old reference] [new reference] [pinned-output diff]
expect() {
  local name="$1" want="$2" title="$3" files="$4"
  local messages="${5:-}" old="${6:-$reference_v1}" new="${7:-$reference_v1}" pinned="${8:-}"
  printf '%s\n' "$files" >"$work/files"
  printf '%s\n' "$messages" >"$work/messages"
  printf '%s' "$old" >"$work/old"
  printf '%s' "$new" >"$work/new"
  printf '%s\n' "$pinned" >"$work/pinned"
  local got=pass
  "$here/docs-follow.sh" decide "$work/files" "$work/messages" "$title" \
    "$work/old" "$work/new" "$work/pinned" >"$work/out" 2>&1 || got=fail
  if [ "$got" != "$want" ]; then
    echo "FAIL: $name -> $got, want $want"
    sed 's/^/    /' "$work/out"
    failures=$((failures + 1))
  fi
}

expect "docs only" pass "docs: Fix a typo" "docs/guides/x.md"
expect "refactor of src" pass "refactor: Rename a helper" "src/IviCli.Cli/A.cs"
expect "fix without changelog" fail "fix: Escape a quote" "src/IviCli.Cli/A.cs"
expect "fix with changelog" pass "fix: Escape a quote" "src/IviCli.Cli/A.cs
docs/CHANGELOG.md"
expect "scoped feat without changelog" fail "feat(serial): Open ports" "src/IviCli.Backends.Serial/A.cs"
expect "perf without changelog" fail "perf: Pool buffers" "src/IviCli.Server/A.cs"
expect "breaking chore without changelog" fail "chore!: Drop a setting" "src/IviCli.Cli/A.cs"
expect "fix touching only tests" pass "fix: Flaky timing" "tests/IviCli.Server.Tests/A.cs"
expect "no title: a feat commit in the range decides" fail "" "src/IviCli.Cli/A.cs" \
  "refactor: Move a helper
feat: Add a flag"
expect "no title: only refactor commits" pass "" "src/IviCli.Cli/A.cs" "refactor: Move a helper"

expect "reference reworded without PRD" fail "docs: Reword help" "docs/cli-reference.md
docs/CHANGELOG.md" "" "$reference_v1" "$reference_v2_reworded"
expect "reference reworded with PRD and changelog" pass "docs: Reword help" "docs/cli-reference.md
docs/CHANGELOG.md
docs/PRD.md
docs/PRD.jp.md" "" "$reference_v1" "$reference_v2_reworded"
expect "command added without README" fail "feat: Add visa wipe" "src/IviCli.Cli/A.cs
docs/cli-reference.md
docs/CHANGELOG.md
docs/PRD.md
docs/PRD.jp.md" "" "$reference_v1" "$reference_v2_added"
expect "command added with README" pass "feat: Add visa wipe" "src/IviCli.Cli/A.cs
docs/cli-reference.md
docs/CHANGELOG.md
docs/PRD.md
docs/PRD.jp.md
README.md
docs/README.jp.md" "" "$reference_v1" "$reference_v2_added"

expect "reference introduced" pass "docs: Add the reference" "docs/cli-reference.md" "" "" "$reference_v1"

expect "pinned output changed without PRD" fail "fix: Escape JSON" \
  "tests/IviCli.Cli.Tests/Commands/AFormatterTests.cs
src/IviCli.Cli/A.cs
docs/CHANGELOG.md" "" "" "" '-            .ShouldBe("""{"a":1}""");'
expect "pinned output only added to" pass "test: Pin JSON" \
  "tests/IviCli.Cli.Tests/Commands/AFormatterTests.cs" "" "" "" '+            .ShouldBe("""{"a":1}""");'

expect "PRD without its mirror" fail "docs: Edit PRD" "docs/PRD.md"
expect "README with its mirror" pass "docs: Edit README" "README.md
docs/README.jp.md"
expect "conformance mirror alone" fail "docs: Edit" "docs/conformance.jp.md"

expect "trailer lifts the changelog rule" pass "fix: Internal retry" "src/IviCli.Cli/A.cs" \
  "fix: Internal retry

Docs-Unaffected: the retry is not observable"
expect "empty trailer lifts nothing" fail "fix: Internal retry" "src/IviCli.Cli/A.cs" \
  "fix: Internal retry

Docs-Unaffected:"
expect "trailer does not lift the pair rule" fail "docs: Edit PRD" "docs/PRD.md" \
  "docs: Edit PRD

Docs-Unaffected: wording only"

if [ "$failures" -ne 0 ]; then
  echo "$failures case(s) failed"
  exit 1
fi
echo "docs-follow: all cases pass"
