# 0027. Phase 3 — Operator-Facing Automation

- Status: Accepted

## Context

Phase 1 (single-host VISA CLI) and Phase 2 (gateway servers) run one
command at a time against a local instrument or the gateway. An operator
who repeats a sequence, watches a reading, or captures traffic for a mock
needs the CLI to drive either of them over time. PRD §15 lists several
future extensions; this ADR picks the minimal subset that turns the CLI
from a single-shot tool into an automation surface.

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

A script file is UTF-8 text, read one line at a time; blank lines are
skipped. A line beginning with `!` is an ivi-cli directive, and every
other line is SCPI sent to the instrument as written
([ADR 0051](0051-scpi-text-interpretation.md) §6 gives the reasons):

| Line | Semantics |
| --- | --- |
| SCPI with no header ending in `?` | `IIviBackend.WriteAsync` |
| SCPI with a header ending in `?` (ADR 0051 §7) | `IIviBackend.QueryAsync`, response echoed |
| `!sleep <ms>` | `Task.Delay(ms, ct)` |
| `!assert <regex>` | regular expression matched against the last query response |
| `!values <pattern>` | slots and predicates checked against the last query response (ADR 0051 §5) |
| `!echo <text>` | `text` written verbatim to stdout |
| `!#` ... | comment |

Directive keywords are case-insensitive. A failed `!assert` or `!values`
stops execution with a non-zero exit code (per ADR 0011). A script with a
line that cannot be parsed is refused before any line runs. The parser
is implemented as a pure function in Application; the handler is the only
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
- PRD §15 owns the future extensions this ADR does not pick.

## References

- PRD §14 — the Phase 3 list.
- ADR 0010 — handler-level CQRS architecture.
- ADR 0015 — threading model (per-Task fan-out unchanged).
- ADR 0017 — logging and structured output.
- ADR 0026 — Mock Scenario System (extended by §4 above).
