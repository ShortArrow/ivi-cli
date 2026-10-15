#!/usr/bin/env bash
# Checks spec-trace.sh against ADR 0054: every cited clause has a test,
# every tagged clause is cited, every entry cites a clause, every
# "Deviates" entry links an issue, and the Japanese Spec column matches.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dir="$(mktemp -d)"
trap 'rm -rf "$dir"' EXIT

en() {
  cat >"$dir/en.md" <<EOF
# Standards conformance

| Kind | Behaviour | Spec |
| --- | --- | --- |
| Follows | Error 15 is a timeout. | VXI-11 RULE B.6.19 |
| ivi-cli defines | Reopen after a timeout. | ADR 0053 §3, VXI-11 OBSERVATION B.4.7 |
$1
EOF
}

jp() {
  cat >"$dir/jp.md" <<EOF
# 規格への準拠

| 種類 | 振る舞い | Spec |
| --- | --- | --- |
| 準拠 | エラー 15 はタイムアウト。 | VXI-11 RULE B.6.19 |
| ivi-cli の定め | タイムアウトのあとに開き直す。 | ADR 0053 §3, VXI-11 OBSERVATION B.4.7 |
$1
EOF
}

tags() {
  printf '%s\n' "$@" >"$dir/tags.tsv"
}

failures=0
expect() {
  local name="$1" want="$2" out status=0
  out="$("$here/spec-trace.sh" "$dir/tags.tsv" "$dir/en.md" "$dir/jp.md" 2>&1)" || status=$?
  if [ "$want" = pass ] && [ "$status" -ne 0 ]; then
    echo "FAIL: $name: want pass, got exit $status: $out"
    failures=$((failures + 1))
  elif [ "$want" != pass ] && { [ "$status" -eq 0 ] || ! grep -qF -- "$want" <<<"$out"; }; then
    echo "FAIL: $name: want failure naming '$want', got exit $status: $out"
    failures=$((failures + 1))
  fi
}

all_tags=(
  $'VXI-11 RULE B.6.19\tA.Error15'
  $'ADR 0053 §3\tA.Reopen'
  $'VXI-11 OBSERVATION B.4.7\tA.Reopen'
)

en ''
jp ''
tags "${all_tags[@]}"
expect "matching document and tests" pass

tags "${all_tags[@]}" $'VXI-11 RULE B.6.19\tA.Error15Again'
expect "a clause with two tests" pass

tags $'VXI-11 RULE B.6.19\tA.Error15' $'ADR 0053 §3\tA.Reopen'
expect "a cited clause no test carries" "VXI-11 OBSERVATION B.4.7"

tags "${all_tags[@]}" $'IVI-6.1 §2.6\tA.Lock'
expect "a tagged clause no entry cites" "IVI-6.1 §2.6"
expect "the test carrying an uncited clause is named" "A.Lock"

tags "${all_tags[@]}"
en '| ivi-cli defines | No clause here. | — |'
jp '| ivi-cli の定め | 条項がない。 | — |'
expect "an entry citing no clause" "No clause here."

en '| ivi-cli defines | Empty cell. |  |'
jp '| ivi-cli の定め | 空の欄。 |  |'
expect "an entry with an empty Spec cell" "Empty cell."

tags "${all_tags[@]}" $'VXI-11 B.4.3\tA.Local'
en '| Deviates | Local ignores io_timeout. | VXI-11 B.4.3 |'
jp '| 逸脱 | ローカルは io_timeout を無視する。 | VXI-11 B.4.3 |'
expect "a deviation linking no issue" "Local ignores io_timeout."

en '| Deviates | Local ignores io_timeout ([#266](https://github.com/ShortArrow/ivi-cli/issues/266)). | VXI-11 B.4.3 |'
expect "a deviation linking its issue" pass

jp '| 逸脱 | ローカルは io_timeout を無視する。 | VXI-11 B.4.4 |'
expect "a Japanese Spec cell that differs" "jp.md"

jp ''
expect "a Japanese table with a row missing" "jp.md"

if [ "$failures" -ne 0 ]; then
  echo "$failures case(s) failed"
  exit 1
fi
echo "spec-trace: all cases pass"
