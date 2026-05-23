# 0027. Phase 3 — Operator-Facing Automation

- Status: Accepted
- Date: 2026-05-23

## Context

PRD §14 originally enumerated only Phase 1 (single-host VISA CLI) and
Phase 2 (gateway servers). Once Phase 2 landed (commits `acd1736` …
`78b1bd9`), the natural next step is operator-facing automation against
either a local instrument or the gateway. PRD §15 lists several future
extensions; this ADR picks the minimal subset that turns the CLI from a
single-shot tool into an automation surface, and locks the design before
implementation.

The four features promoted into Phase 3 — `visa script`, `visa monitor`,
`mock scenario record`, `server log` — all share a common shape:
**iterative execution against an existing Device / Server**, producing
structured output suitable for piping into external tooling. They also
deliberately reuse existing ports (`IBackendFactory`, `IConfigStore`,
`IScenarioStore`, `IFileSystem`) without introducing new domain
concepts, keeping the Domain frozen after Phase 2.

## Decision

### 1. Scope

In Phase 3:

- `visa script <file>` — execute a `.scpi` script line-by-line against
  the session-active device (or `--device <name>`).
- `visa monitor <query> [--interval N] [--count N]` — repeated query
  with timestamped output, until Ctrl+C or `--count` exhausts.
- `mock scenario record <name>` — wrap the FakeBackend (and only the
  FakeBackend) such that every observed write / query is appended to
  the scenario file under the state directory.
- `server log <server>` — tail the per-server structured log file.

Out of scope (deferred to Phase 4+): waveform streaming, Web UI,
authentication, VXI-11 server, AI integration. PRD §15 retains the
backlog.

### 2. Script File Format

A script file is UTF-8 text. Each non-blank, non-`#`-prefixed line is
one directive. Supported directives:

| Directive  | Syntax                  | Semantics                          |
| ---------- | ----------------------- | ---------------------------------- |
| write      | `<scpi>` (no `?`)       | `IIviBackend.WriteAsync`           |
| query      | `<scpi>?` (ends with ?) | `IIviBackend.QueryAsync`, echoed   |
| sleep      | `sleep <ms>`            | `Task.Delay(ms, ct)`               |
| assert     | `assert <regex>`        | match against last query response  |
| echo       | `echo <text>`           | write `text` verbatim to stdout    |

A failed `assert` returns a non-zero exit code (per ADR 0011) and stops
execution. Comments use `#` (line) and `# ...` (trailing). The parser is
implemented as a pure function in Application; the handler is the only
impure surface (sequence of awaited Backend calls plus stdout).

### 3. Monitor Output

`visa monitor <query>` prints one line per sample:

```
2026-05-23T14:02:11.482Z  *IDN?  FAKE,GEN,0,1.0
```

`--json` switches to one JSON object per line. The handler reuses
`IBackendFactory.CreateFor` and treats cancellation (Ctrl+C) as a clean
exit code, not an error.

### 4. Scenario Recording

`mock scenario record <name>` activates a wrapper IIviBackend that
delegates to FakeBackend and, after every successful Read / Query,
emits a `[[scenes]]` entry to the scenario file via `IScenarioStore`
extended with an `AppendSceneAsync` method (additive change; no
breaking re-shape).

Recording is mutually exclusive with playback: activating record while
a scenario is loaded for playback is an error.

### 5. Server Log Tail

`server log <name>` resolves the server's log file via the existing
log-directory path (`IviPaths.ResolveLogDirectory()` + per-server
suffix) and tails it. The implementation is read-only — no IPC to the
running gateway is involved; the gateway already writes structured
JSON lines per ADR 0017.

### 6. Threading

All Phase 3 commands run on the calling thread and use cooperative
cancellation via the shared `CancellationToken` from `Console.CancelKeyPress`.
No new threading model is introduced (per ADR 0015).

### 7. Configuration & Domain Surface

Phase 3 does **not** add new Domain entities. Script files are pure
data, not persisted configuration. `mock scenario record` reuses the
Phase 1.5 `MockScenario` aggregate (ADR 0026) and its store; the only
extension is an additive `AppendSceneAsync(name, scene)` on the store
interface. ConfigDocument is frozen.

### 8. Out-of-band Concerns

- Errors map to existing `IviError` variants (no new error families).
- Logging uses the Serilog template policy from ADR 0017.
- Tests use the Phase 1 patterns: handler unit tests with
  `FakeBackend` / `FakeConfigStore`, plus CLI integration via the
  System.CommandLine parser (no spawning the binary in tests).

## Consequences

- Phase 3 adds four CLI verbs and one Application handler per verb.
  No new Domain types, no new Backend transports, no Domain breakages.
- The `IScenarioStore` interface grows by one method
  (`AppendSceneAsync`); existing TOML store implementation is the only
  consumer that needs an update.
- Once Phase 3 is accepted and implemented, PRD §15 still owns the
  remaining future extensions; further phases are an open question.

## References

- PRD §14 (extended) — Phase 3 added.
- ADR 0010 — handler-level CQRS architecture.
- ADR 0015 — threading model (per-Task fan-out unchanged).
- ADR 0017 — logging and structured output.
- ADR 0026 — Mock Scenario System (extended by §4 above).
