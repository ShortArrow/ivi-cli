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

## Until #25 lands: set `IVICLI_MOCK_ONLY=1` before `server start`

When traffic comes through the gateway (not the local CLI), the
backend factory dispatches on the device's resource shape
**before** consulting the active scenario, so
`TCPIP0::127.0.0.1::INSTR` falls into the VXI-11 path and the
gateway tries (and fails) to open a real VXI-11 socket to
127.0.0.1:1024. Until [#25](https://github.com/ShortArrow/ivi-cli/issues/25)
makes the factory scenario-aware, force the all-fallback mode:

```sh
IVICLI_MOCK_ONLY=1 ivicli server start hislip-psu
```

```powershell
$env:IVICLI_MOCK_ONLY = '1'
ivicli server start hislip-psu
```

Both `setup.sh` and `setup.ps1` set this env var on your behalf.

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
