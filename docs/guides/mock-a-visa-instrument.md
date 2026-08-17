# Mock a VISA instrument

You are building an app that drives a VISA instrument (over HiSLIP, VXI-11,
or a raw SCPI socket) and want to test it without the real hardware on the
bench. This guide stands up a **mock instrument** that answers your app's
SCPI, so your test suite talks to `ivicli` instead of a physical device.

The flow is always three steps:

1. **Author** a *scenario* — the SCPI request/response map for your instrument.
2. **Serve** it over a gateway (HiSLIP `4880` and/or raw SOCKET `5025`).
3. **Point your app** at the gateway's VISA resource string.

If you just want *a* mock to smoke-test against, jump to
[Run a ready-made mock](#run-a-ready-made-mock). To model *your* instrument,
start at [Author a scenario](#author-a-scenario).

---

## Run a ready-made mock

The published container serves a built-in scenario (`*IDN?` / `*RST` /
`*OPC?` / `SYST:ERR?`) with no install and no config:

```sh
docker run --rm -p 4880:4880 -p 5025:5025 \
    ghcr.io/shortarrow/ivi-cli-mock:latest
```

Your app connects to `TCPIP::localhost::hislip0::INSTR` (HiSLIP) or
`TCPIP::localhost::5025::SOCKET` (raw socket). That is enough to prove your
transport wiring; to model your instrument's actual behaviour, author a
scenario.

---

## Author a scenario

A scenario has an identity (`*IDN?` string), one or more **scenes** (states),
and **rules** that map an incoming SCPI line to a response. Rules can flip the
active scene, which is how you model stateful instruments (output on/off,
range changes, …).

You can author it two ways — from the CLI, or by hand-writing a TOML file.

### From the CLI

```sh
# 1. Create the scenario with its identity and initial scene.
ivicli mock scenario create my-dmm --idn 'ACME,DMM-1000,SN42,2.1' --initial idle

# 2. Add rules to a scene. Queries respond; writes acknowledge.
ivicli mock scenario rule add my-dmm --in idle --match '*IDN?'     --respond 'ACME,DMM-1000,SN42,2.1'
ivicli mock scenario rule add my-dmm --in idle --match '*RST'      --ack
ivicli mock scenario rule add my-dmm --in idle --match 'MEAS:VOLT?' --respond '3.271'
ivicli mock scenario rule add my-dmm --in idle --match 'SYST:ERR?' --respond '0,"No error"'

# 3. Activate it so the mock gateway serves it.
ivicli mock scenario activate my-dmm
```

Add a second scene and a transition to model state:

```sh
ivicli mock scenario scene add my-dmm measuring
ivicli mock scenario rule add my-dmm --in idle      --match 'INIT'  --ack --transition-to measuring
ivicli mock scenario rule add my-dmm --in measuring --match 'FETC?' --respond '3.271'
ivicli mock scenario rule add my-dmm --in measuring --match 'ABOR'  --ack --transition-to idle
```

### By hand-writing TOML

The same scenario is a plain TOML file. Drop it in the scenarios directory
(see [Where scenarios live](#where-scenarios-live)) and `ivicli mock scenario
activate my-dmm`, or `ivicli mock scenario import ./my-dmm.toml`.

```toml
idn = "ACME,DMM-1000,SN42,2.1"
initial_scene = "idle"

[[scenes]]
name = "idle"

[[scenes.rules]]
match = "*IDN?"
respond = "ACME,DMM-1000,SN42,2.1"

[[scenes.rules]]
match = "INIT"
ack = true
transition_to = "measuring"

[[scenes]]
name = "measuring"

[[scenes.rules]]
match = "FETC?"
respond = "3.271"
```

### Rule vocabulary

| Field (TOML / CLI flag) | Meaning |
| --- | --- |
| `match` / `--match` | The exact SCPI line this rule answers. |
| `respond` / `--respond` | The response text to return (for a query). |
| `ack` / `--ack` | Accept a write with no response body. |
| `fail` / `--fail` (+ `fail_detail` / `--fail-detail`) | Return a SCPI error instead of a normal response. |
| `transition_to` / `--transition-to` | Switch the active scene after this rule fires. |
| `srq` / `--srq` | Status byte (0..255) to raise a service request with after the rule fires; reported verbatim. |

An instrument that answers `*OPC` with a service request is the usual
reason to reach for `srq`:

```toml
[[scenes.rules]]
match = "*ESE 1"
ack = true

[[scenes.rules]]
match = "*SRE 32"
ack = true

[[scenes.rules]]
match = "*OPC"
ack = true
srq = 0x60   # RQS | ESB — the SRQ a real instrument raises when the operation completes
```

The mock keeps no status registers, so `srq` is the whole model: the rule
that stands for the completing operation carries the status byte a real
instrument would report. Put 0x40 in it if you want the RQS bit a serial
poll expects.

> **v0.2.x limitations.** Rules match a full SCPI line literally (no
> parameter capture — `VOLT 7.5` then `VOLT?` still returns the canned
> value), and rule sets are per-scene (static metadata like `*IDN?` is
> repeated in every scene). Both are tracked in issue
> [#26](https://github.com/ShortArrow/ivi-cli/issues/26).

### Quirk profiles

Real instruments misbehave, and code that talks to them has to survive it.
A scenario can ask the mock to reproduce a specific firmware fault through
an optional `[quirks]` table:

```toml
[quirks]
srq_notify_wedge_after = 1
```

`srq_notify_wedge_after` counts service requests delivered to the SRQ
stream. Past that count the mock keeps recording the status byte — a
serial poll still shows the request standing — but no notification is
ever sent again, so a gateway forwarding SRQs to a remote client goes
quiet. This is a Kikusui PWR401L on the bench: after certain session
histories its USB488 notification machinery wedged, and nothing short of
a power cycle brought it back (recorded on PR
[#114](https://github.com/ShortArrow/ivi-cli/pull/114)). Closing and
reopening the device is the mock's power cycle; `0` wedges the stream
before the first notification.

Quirks are hand-written TOML only — there is no CLI flag for them, so
author the file and `ivicli mock scenario import ./wedged.toml`.

---

## Serve it

### Option A — the mock container

Mount your scenarios directory and pick the scenario by name:

```sh
docker run --rm \
  -v "$PWD:/etc/ivi-cli/scenarios" \
  -e IVICLI_SCENARIO=my-dmm \
  -p 4880:4880 -p 5025:5025 \
  ghcr.io/shortarrow/ivi-cli-mock:latest
```

### Option B — the bare CLI

Register a scenario-backed device and expose it through a gateway server:

```sh
ivicli mock scenario activate my-dmm
ivicli visa add dut 'TCPIP0::127.0.0.1::INSTR'      # the scenario-backed device
ivicli server add dmm-srv --type hislip --port 4880 # or --type socket --port 5025
ivicli server route add dmm-srv hislip0 dut         # SOCKET: route the port itself
ivicli server start dmm-srv
```

Either way the mock now listens on:

| Transport | VISA resource |
| --- | --- |
| HiSLIP | `TCPIP::localhost::hislip0::INSTR` |
| Raw SOCKET | `TCPIP::localhost::5025::SOCKET` |

### Swap behaviour on a running mock

You can `activate` a different scenario against a gateway that is already
serving — no restart, no reconnect. The change is picked up on the client's
next query:

```sh
ivicli mock scenario activate my-dmm-faulted   # while the app stays connected
```

The next SCPI operation on the open connection sees the new scenario. An
unchanged binding keeps its in-flight scene, so re-running `activate` with the
same scenario does not reset a state machine the client already advanced. This
works identically over HiSLIP, raw SOCKET, and VXI-11.

---

## Point your app at it

- **`ivicli` or any SCPI client** — use the resource string above:

  ```sh
  ivicli visa add tester 'TCPIP::localhost::hislip0::INSTR'
  ivicli visa query tester '*IDN?'   # → ACME,DMM-1000,SN42,2.1
  ```

- **Apps that go through NI-VISA / Keysight VISA** (they resolve resources
  through the vendor runtime, not ivicli) — register the mock as a static
  TCP/IP resource in **NI MAX** (or Keysight Connection Expert): add a
  *Network Instrument* / *Manual TCP/IP* entry pointing at `localhost` with
  the HiSLIP sub-address `hislip0` or the raw-socket port `5025`. The
  [PSU sample](../samples/psu/) `setup.ps1` prints the exact NI MAX steps at
  the end of its run.

---

## Verify what your app sent

When your app drives the mock through its own VISA stack, the test never sends
raw SCPI itself — so how do you assert the app wrote `:VOLT 24.000` and not, say,
`:VOLT 24` or nothing at all? Start the gateway with traffic capture on, then
read back the writes out of band:

```sh
# Start the mock with capture enabled. Use a FRESH path per test run so a
# previous run's writes don't leak in (see isolation note below).
IVICLI_CAPTURE=run-$RANDOM.ndjson ivicli server start dmm-srv

# ... your app connects and writes :VOLT 24.000 ...

# Last :VOLT write the device received (substring filter):
ivicli mock received dut --match ':VOLT' --capture run-*.ndjson
# → :VOLT 24.000        (exit 0; exit 1 if nothing matched)

# Assert the exact command arrived — ':VOLT' as a substring would also match
# ':VOLT:PROT 30', so use --exact when you mean the whole line:
ivicli mock received dut --exact ':VOLT 24.000' --capture run-*.ndjson

# How many times did the app set the voltage? (--count exits 0 even at 0)
ivicli mock received dut --match ':VOLT' --capture run-*.ndjson --count
# → 1

# Machine-readable — always a JSON array (single element by default, [] if none):
ivicli mock received dut --match ':CURR' --capture run-*.ndjson --json
# → [{"device":"dut","scpi":":CURR 3.300","timestamp":"..."}]
```

`--match` filters by substring and `--exact` by full string (mutually
exclusive); the default reports the most recent matching write (add `--all` to
list every match, oldest first, or `--count` for just the number). Absent
`--count`, a non-zero exit when nothing matched lets a test assert a write did
*not* arrive. The capture is the shared audit log; the reader opens it with
shared access, so you can query it while the gateway is still serving.

**Isolation.** The capture *appends* across runs, so `--all` and the default
"last" can surface writes from an earlier run. Give each test run its own
`IVICLI_CAPTURE` file (or truncate it before the run) — there is deliberately no
time filter.

---

## Record instead of author

If you have the real instrument available once, capture a session and replay
it — no rules to write by hand:

- **Capture → import.** Run any workflow with `IVICLI_CAPTURE=<file>` against
  the real device, then `ivicli mock scenario import <file>` turns the
  captured traffic into a scenario.
- **Record a script.** `ivicli mock scenario record <name> --from-script
  <script>` captures the SCPI of a script run directly into a scenario.

Replay it deterministically anywhere with `IVICLI_REPLAY=<name>`.

---

## Where scenarios live

The CLI looks for scenarios under the platform config directory (a sibling of
`config.toml`):

| OS | Default path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/ivi-cli/scenarios/` (default `~/.config/ivi-cli/scenarios/`) |
| macOS | `~/.config/ivi-cli/scenarios/` |
| Windows | `%LOCALAPPDATA%\ivi-cli\scenarios\` |
| Container | `/etc/ivi-cli/scenarios/` |

Override the root with `IVICLI_CONFIG=<path>`.

---

## Next steps

- **[PSU sample](../samples/psu/)** — a complete, runnable two-state FSM
  (output on/off) with `setup.sh` / `setup.ps1` and the NI MAX steps.
