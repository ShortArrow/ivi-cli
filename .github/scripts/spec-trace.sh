#!/usr/bin/env bash
# Checks the conformance document against the tests' Spec traits (ADR 0054).
# Fails when an entry cites a clause no test carries, a test carries a
# clause no entry cites, an entry cites no clause, a "Deviates" entry links
# no issue, or the Japanese mirror's Spec column differs from the English.
#
# Usage: spec-trace.sh <tags-file> <conformance.md> <conformance.jp.md>
# The tags file has one line per tag: "<clause><TAB><test name>".
set -euo pipefail
tags_file="$1"
en="$2"
jp="$3"

# Separates the fields rows() prints. Not a tab: `read` collapses
# consecutive tabs, which would lose an empty Spec cell.
sep=$'\x1f'

# Prints "<kind><sep><spec cell><sep><row>" for each data row of every table.
rows() {
  awk -F' [|] ' -v sep="$sep" '
    /^[|] *:?-{3,}/ { body = 1; next }
    /^[|]/ && body {
      kind = $1; sub(/^[|] */, "", kind); sub(/ *$/, "", kind)
      spec = $NF; sub(/ *[|]$/, "", spec)
      print kind sep spec sep $0; next
    }
    !/^[|]/ { body = 0 }
  ' "$1"
}

# Prints the header row of each table whose last column is not "Spec".
headers_without_spec() {
  awk '
    /^[|] *:?-{3,}/ { if (header !~ /[|] *Spec *[|] *$/) print header }
    { header = $0 }
  ' "$1"
}

# Prints each clause of a Spec cell on its own line.
clauses() {
  tr -d '\r' | sed 's/, /\n/g' | sed 's/^ *//; s/ *$//' | grep -v -e '^$' -e '^—$' || true
}

failures=0
fail() {
  echo "::error::$1"
  failures=$((failures + 1))
}

en_rows="$(rows "$en")"
cited="$(cut -d"$sep" -f2 <<<"$en_rows" | clauses | sort -u)"
tagged="$(cut -f1 "$tags_file" | tr -d '\r' | sort -u)"

for doc in "$en" "$jp"; do
  while read -r header; do
    [ -n "$header" ] || continue
    fail "$doc: a table's last column is not Spec: $header"
  done < <(headers_without_spec "$doc")
  while IFS="$sep" read -r kind spec row; do
    [ -n "$row" ] || continue
    if [ -z "$(clauses <<<"$spec")" ]; then
      fail "$doc: an entry cites no clause: $row"
    fi
    if { [ "$kind" = Deviates ] || [ "$kind" = 逸脱 ]; } && ! grep -qE '/issues/[0-9]+' <<<"$row"; then
      fail "$doc: a Deviates entry links no issue: $row"
    fi
  done < <(rows "$doc")
done

while read -r clause; do
  [ -n "$clause" ] || continue
  fail "$en cites '$clause', but no test carries [Spec] for it"
done < <(comm -23 <(printf '%s\n' "$cited") <(printf '%s\n' "$tagged"))

while read -r clause; do
  [ -n "$clause" ] || continue
  tests="$(awk -F'\t' -v c="$clause" '$1 == c { print $2 }' "$tags_file" | tr -d '\r' | sort -u | paste -sd, - | sed 's/,/, /g')"
  fail "tests carry [Spec] '$clause', which $en does not cite: $tests"
done < <(comm -13 <(printf '%s\n' "$cited") <(printf '%s\n' "$tagged"))

if [ "$(cut -d"$sep" -f2 <<<"$en_rows")" != "$(rows "$jp" | cut -d"$sep" -f2)" ]; then
  fail "the Spec column of $jp differs from $en, row for row"
fi

if [ "$failures" -ne 0 ]; then
  echo "$failures problem(s) between the tests and $en"
  exit 1
fi
echo "spec-trace: $(grep -c . <<<"$cited") clause(s) cited, each with a test"
