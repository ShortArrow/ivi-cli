# Sample — PSU (DC power supply) mock VISA device

This sample shows how to spin up a programmable DC power supply
mock that responds to a typical SCPI conversation
(`*IDN?` / `*RST` / `OUTP ON` / `VOLT 5.0` / `MEAS:VOLT?` / …)
over HiSLIP or raw SOCKET — no real hardware, no .NET install
required if you use the mock container.

## Files

| File | Purpose |
|---|---|
| `psu-bench.toml` | The scenario itself — drop into a scenarios directory. |
| `setup.sh` | One-shot bash script that walks every CLI step. |

## Quick start — bare CLI

```sh
./setup.sh
ivicli visa add tester TCPIP::localhost::hislip0::INSTR
ivicli visa query tester "*IDN?"
# → IVICLI-MOCK,PSU,SN0001,1.0.0
```

`setup.sh` accepts `PROTO=socket PORT=5025` (and `PORT=...` /
`SUBADDR=...`) to route through the raw-SOCKET gateway or a
different TCP port.

## Quick start — mock-VISA container

```sh
docker run --rm \
  -v $PWD:/etc/ivi-cli/scenarios \
  -e IVICLI_SCENARIO=psu-bench \
  -p 4880:4880 -p 5025:5025 \
  ghcr.io/shortarrow/ivi-cli-mock:latest
```

Then from any other host or terminal:

```sh
ivicli visa add tester TCPIP::localhost::hislip0::INSTR
ivicli visa query tester "*IDN?"
# → IVICLI-MOCK,PSU,SN0001,1.0.0
```

The container exposes both HiSLIP (4880) and raw SOCKET (5025);
the same scenario serves both.

## What the scenario covers

| SCPI | Direction | Mock response |
|---|---|---|
| `*IDN?` | query | `IVICLI-MOCK,PSU,SN0001,1.0.0` |
| `*RST` | write | (ack) |
| `*OPC?` | query | `1` |
| `OUTP ON` / `OUTP OFF` | write | (ack) |
| `OUTP?` | query | `1` |
| `VOLT 5.0` | write | (ack) |
| `VOLT?` | query | `5.000` |
| `CURR 1.0` | write | (ack) |
| `CURR?` | query | `1.000` |
| `MEAS:VOLT?` | query | `4.998` |
| `MEAS:CURR?` | query | `0.823` |
| `SYST:ERR?` | query | `0,"No error"` |

## Limitations

- **No state machine** — `OUTP ON` followed by `OUTP?` still
  returns the canned `1`, regardless of what was previously set.
  For a stateful mock, capture a real instrument session with
  `ivicli mock scenario record --from-script` and import it as
  a new scenario.
- **Static measurements** — `MEAS:VOLT?` returns the same value
  every time. Replace with a `record` capture for realistic
  drift.
- **Single device per HiSlip server** — to expose multiple
  instruments today, use separate `server add` entries on
  separate ports. Tracked in
  [#21](https://github.com/ShortArrow/ivi-cli/issues/21).

## Where the scenario lives

The CLI looks for scenarios under the platform-specific config
directory:

| OS | Default path |
|---|---|
| Linux | `$XDG_CONFIG_HOME/ivi-cli/scenarios/` (default `~/.config/ivi-cli/scenarios/`) |
| macOS | `~/.config/ivi-cli/scenarios/` |
| Windows | `%LOCALAPPDATA%\ivi-cli\scenarios\` |
| Container | `/etc/ivi-cli/scenarios/` |

Override via `IVICLI_CONFIG=<path>` (the scenarios directory
defaults to a sibling of `config.toml`).
