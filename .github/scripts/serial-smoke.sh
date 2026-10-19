#!/bin/sh
# Serial round trip for a published ivicli: the Serial backend opens one
# end of a socat pseudo-terminal and a stand-in instrument answers *IDN?
# on the other (ADR 0055 §11). A publish that leaves out System.IO.Ports'
# native library, or trims what the backend needs, fails here.
#
# Usage: serial-smoke.sh <ivicli>
# Needs socat. Uses its own config.toml, so it leaves the caller's alone.
set -eu

bin="${1:?usage: serial-smoke.sh <ivicli>}"
expected='IVICLI-SMOKE,serial,0,0'

dir="$(mktemp -d)"
socat_pid=''
cleanup() {
  [ -n "$socat_pid" ] && kill "$socat_pid" 2>/dev/null || true
  rm -rf "$dir"
}
trap cleanup EXIT

cat >"$dir/instrument.sh" <<EOF
#!/bin/sh
while IFS= read -r line; do
  case "\$line" in
    '*IDN?'*) printf '%s\n' '$expected' ;;
  esac
done
EOF
chmod +x "$dir/instrument.sh"

socat "pty,raw,echo=0,link=$dir/port" "EXEC:$dir/instrument.sh" &
socat_pid=$!
i=0
until [ -e "$dir/port" ]; do
  i=$((i + 1))
  if [ "$i" -gt 50 ]; then
    echo "serial smoke FAILED: socat did not create the port"
    exit 1
  fi
  sleep 0.1
done

export IVICLI_CONFIG="$dir/config.toml"
export IVICLI_MOCK_ONLY=0
"$bin" visa add serial-smoke "ASRL$dir/port::INSTR" \
  --read-termination lf --write-termination lf
reply="$("$bin" visa query serial-smoke '*IDN?')"
echo "got (serial): $reply"
if [ "$reply" = "$expected" ]; then
  echo "serial smoke passed"
else
  echo "serial smoke FAILED: expected $expected"
  exit 1
fi
