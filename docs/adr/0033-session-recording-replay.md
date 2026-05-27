# 0033. Session recording / replay (capture → import → replay)

- Status: Accepted
- Date: 2026-05-27

## Context

ADR 0028 already ships the `ReplayBackend` that turns a stored
`MockScenario` into an `IIviBackend`; Batch F (ADR 0031) ships the
`IVICLI_CAPTURE` env var that records every backend op as an NDJSON
file. PRD §15 still listed "session recording/replay" as Planned —
the missing piece is the **bridge**: convert a captured NDJSON file
into a stored `MockScenario` the existing replay machinery can serve.

This ADR records the conversion contract and the import verb so a
future contributor can find both the "why" and the "out of scope" in
one place.

## Decision

### 1. Event → Scene mapping

| `TrafficOp` | `Ok` | Produces |
| --- | --- | --- |
| `Write`         | `true`  | `MockScene(Data, SceneAction.Ack)` |
| `Query`         | `true`  | `MockScene(Data, SceneAction.Respond(Response ?? ""))` |
| `Read`          | `true`  | skipped — no natural `Match` key |
| `Open` / `Close`| any     | skipped — session boundaries |
| any             | `false` | skipped — failure replay deferred |

The first `Query "*IDN?"` Ok=true event in the stream (case-
insensitive match on `"*IDN?"`) populates the resulting scenario's
`IdnDefault`, mirroring how `mock scenario record --from-script`
behaves.

### 2. Device disambiguation

A single `IVICLI_CAPTURE` session can hold events from several
device aliases. The converter:

- Returns `ConvertTrafficMultipleDevices(devices)` when the unfiltered
  event stream covers two or more devices.
- Returns `ConvertTrafficNoScenes(deviceFilter?)` when the filtered
  stream contains no replayable Write / Query events.

The CLI maps both to a usage error and tells the operator which
devices were observed.

### 3. CLI surface

```bash
ivicli mock scenario import <path> --name <scenario> \
       [--device <alias>] [--force]
```

- `<path>` — NDJSON capture file (typically the output of
  `IVICLI_CAPTURE=<path>`).
- `--name` — required; the scenario alias to store under.
- `--device` — required when the capture covers multiple aliases.
- `--force` — overwrite an existing scenario with the same name.

Exit codes follow ADR 0014:

| Outcome | Exit |
| --- | --- |
| Success | 0 |
| Invalid name / device / IO failure / multi-device without filter | `UsageError` |
| Scenario already exists without `--force` | `ConfigurationError` |
| Unexpected failure | `GenericFailure` |

### 4. Layer placement

- **NDJSON reader** — port `IviCli.Application.Capture.INdjsonTrafficReader`,
  adapter `IviCli.Infrastructure.Capture.NdjsonTrafficReader` (opens
  with `FileShare.ReadWrite` so a still-being-written capture can be
  imported in parallel).
- **Converter** — `IviCli.Application.Mock.ITrafficScenarioConverter`
  + `DefaultTrafficScenarioConverter`. Pure function, no IO.
- **Handler** — `ImportScenarioFromTrafficCommandHandler` wires
  reader + converter + `IScenarioStore`.
- **Verb** — `MockScenarioCommand.BuildImport`.

No new top-level namespace; the import lands alongside `record` /
`activate` / `show` under `mock scenario` because it produces the
same `MockScenario` artefact.

### 5. End-to-end flow

```bash
# Capture
IVICLI_CAPTURE=run.ndjson ivicli visa query psu1 "*IDN?"
IVICLI_CAPTURE=run.ndjson ivicli visa write psu1 "OUTP ON"

# Import
ivicli mock scenario import run.ndjson --name psu1-smoke

# Replay
IVICLI_REPLAY=psu1-smoke ivicli visa query psu1 "*IDN?"
```

No new replay runtime — `IVICLI_REPLAY` and `ReplayBackend` already
handle playback once the scenario is stored.

## Out of scope (v2 candidates)

- **Failure replay** — capturing `Ok=false` and emitting
  `SceneAction.Fail(variant, detail)` requires a mapping table from
  `BackendError.Message` to canonical variants. Future ADR.
- **Read-event replay** — bare `device_read` calls without a matching
  `Query` have no `Match` key in v1 of ADR 0026; needs match-shape
  extension first.
- **Multi-device scenarios** — one scenario covering several aliases.
  `ReplayBackend` is per-Device today; needs backend-factory wiring.
- **Auto-derive `--name` from filename** — one-line follow-up; v1
  forces explicit names for clarity in `mock scenario list`.
- **`mock scenario export`** (reverse direction) — not needed today;
  the NDJSON file is the source of truth.

## Consequences

- Operators can capture once on real hardware and replay the same
  session indefinitely without re-occupying the instrument.
- The import is one verb; nothing new in the replay path.
- PRD §15 Planned trims to four entries.
- The Application layer keeps zero `System.IO` references — the
  reader / adapter split lives in Infrastructure.
