# Sample — PSU (DC power supply) mock VISA device

This sample shows how to spin up a programmable DC power supply
mock that responds to a typical SCPI conversation
(`*IDN?` / `*RST` / `OUTP ON` / `VOLT 5.0` / `MEAS:VOLT?` / …)
over HiSLIP or raw SOCKET — no real hardware, no .NET install
required if you use the mock container.

**v0.2.0**: this sample is a **two-state FSM**. `OUTP ON` while the
device is off transitions it to "on"; `OUTP OFF` returns it to
"off". `OUTP?` answers `0` or `1` according to the current state,
and `MEAS:VOLT?` / `MEAS:CURR?` flip between the noise-floor
values (off) and the configured setpoint (on).

## Files

| File | Purpose |
|---|---|
| `psu-bench.toml` | The scenario itself — drop into a scenarios directory. |
| `setup.sh` | bash idempotent walker for Linux / macOS / WSL / Git Bash. |
| `setup.ps1` | PowerShell-native equivalent for Windows. |

## Quick start — bare CLI

### Linux / macOS / WSL / Git Bash

```sh
./setup.sh
ivicli visa add tester TCPIP::localhost::hislip0::INSTR
ivicli visa query tester "*IDN?"
# → IVICLI-MOCK,PSU,SN0001,1.0.0
```

`setup.sh` accepts `PROTO=socket PORT=5025` (and `PORT=...` /
`SUBADDR=...`) to route through the raw-SOCKET gateway or a
different TCP port.

### Windows / PowerShell 7+

```powershell
.\setup.ps1
ivicli visa add tester 'TCPIP::localhost::hislip0::INSTR'
ivicli visa query tester '*IDN?'
# → IVICLI-MOCK,PSU,SN0001,1.0.0
```

Accepts the same overrides as parameters:
`.\setup.ps1 -Proto socket -Port 5025`. Tail of the script
also prints the **NI MAX manual-registration steps** for apps
that go through NI-VISA / Keysight VISA (e.g. ImageDataGetter)
and need a static resource entry to pick the mock up.

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

| SCPI | Direction | State `off` | State `on` |
|---|---|---|---|
| `*IDN?` | query | `IVICLI-MOCK,PSU,SN0001,1.0.0` | (same) |
| `*RST` | write | ack + transition to `off` | (same) |
| `*OPC?` | query | `1` | (same) |
| `OUTP ON` | write | ack + transition to `on` | ack (no-op) |
| `OUTP OFF` | write | ack (no-op) | ack + transition to `off` |
| `OUTP?` | query | `0` | `1` |
| `VOLT 5.0` | write | (ack) | (ack) |
| `VOLT?` | query | `5.000` | `5.000` |
| `CURR 1.0` | write | (ack) | (ack) |
| `CURR?` | query | `1.000` | `1.000` |
| `MEAS:VOLT?` | query | `0.001` (noise floor) | `4.998` |
| `MEAS:CURR?` | query | `0.000` | `0.823` |
| `SYST:ERR?` | query | `0,"No error"` | (same) |

## Limitations

- **No key-value variable state** — `VOLT 7.5` followed by `VOLT?`
  still returns the canned `5.000`. Modelling continuous setpoints
  is a future feature (see issue
  [#26](https://github.com/ShortArrow/ivi-cli/issues/26)
  "Out of scope").
- **Rules don't share across scenes** — static metadata
  (`*IDN?`, etc.) must be duplicated in every scene because v0.2.0
  rule matching is per-scene. The duplication is visible in
  `psu-bench.toml` and `setup.{sh,ps1}`.
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
