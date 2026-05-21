# 0011. Logging and Tracing

- Status: Accepted
- Date: 2026-05-21

## Context

PRD §12 commits to `Microsoft.Extensions.Logging` (MEL) as the logging abstraction. ADR 0014 fixed the contract between errors and the logger: errors expose `Level / Message / LogArgs / Cause` via `IviError`, and all logging happens at a single point — the composition root. ADR 0017 added the masking rule (`ToLogString()` on Value Objects).

What is left to determine is the **logger configuration**: which backend provider, which sinks, where files live, how users control verbosity, the structured-logging conventions in code, and how tests substitute the logger. Tracing (`Activity` / OpenTelemetry) is also decided here at a policy level; details for the Phase 2 gateway are deferred to ADR 0019.

## Decision

### 1. Backend provider: Serilog (bridged through MEL)

The logging backend is **Serilog**, wired into the MEL pipeline via `Serilog.Extensions.Hosting`. Application code depends only on `ILogger<T>` from MEL; Serilog is invisible above the composition root.

Sinks adopted in Phase 1:

- `Serilog.Sinks.Console` — human-readable output to stderr.
- `Serilog.Sinks.File` — rolling JSON file.

Rationale: Serilog provides first-class structured logging, built-in rolling file support, and a mature sink ecosystem (OpenTelemetry, syslog, etc.) for Phase 2. Staying inside the MEL abstraction keeps the rest of the codebase unaware of the backend choice.

### 2. Sinks per phase

| Sink | Phase 1 | Phase 2 |
| --- | --- | --- |
| stderr (console) | enabled | enabled |
| Rotating file (JSON) | enabled | enabled, longer retention |
| syslog / journald | disabled | optional |
| OTLP exporter | disabled | optional (gateway server) |

Both Phase 1 sinks are enabled by default. `--quiet` and `--log-file` modify console behavior or file path; they do not disable the file sink wholesale (the file is the durable debug record).

### 3. Log file location

| OS | Default path |
| --- | --- |
| Linux | `$XDG_STATE_HOME/ivi-cli/logs/ivi-cli-YYYYMMDD.log` (fallback `~/.local/state/ivi-cli/logs/`) |
| macOS | `~/Library/Logs/ivi-cli/ivi-cli-YYYYMMDD.log` |
| Windows | `%LOCALAPPDATA%\ivi-cli\logs\ivi-cli-YYYYMMDD.log` |

- **Rotation**: daily rollover; per-file cap 100 MB; retain 30 days.
- **Permissions**: log directory and files are **user-only** (0600 on Unix, ACL granting only the current user on Windows), per ADR 0017.
- **Overrides**:
  - Environment variable `IVICLI_LOG_DIR` overrides the directory.
  - CLI flag `--log-file=<path>` overrides for a single invocation.

### 4. Format

- **Console (stderr)** — human-readable, lightly colored, compact:

  ```
  [10:23:45 INF] visa.scan completed devices=3
  [10:23:47 WRN] device 'psu1' not found
  ```

- **File** — structured JSON, one record per line (CLEF-compatible):

  ```json
  {"@t":"2026-05-21T10:23:45Z","@l":"Information","@m":"visa.scan completed devices=3","@i":"a4b2","Devices":3,"Command":"visa.scan"}
  ```

  Phase 2 adds `trace_id` / `span_id` once `Activity`-based tracing is introduced (per ADR 0019).

### 5. CLI verbosity and global flags

The following are root-level options on every command (registered once on the System.CommandLine root, not per subcommand):

| Flag | Effect |
| --- | --- |
| (none) | Minimum level `Information`. |
| `-v` / `--verbose` | Minimum level `Debug`. |
| `-vv` | Minimum level `Trace`. |
| `-q` / `--quiet` | Console minimum level `Warning`. The file sink is unaffected. |
| `--log-file=<path>` | Overrides the file destination for this invocation. |
| `--log-format=human\|json` | Switches the console sink format. Default `human`. |

Verbosity flags affect both sinks (raising the minimum level). `--quiet` is asymmetric — it suppresses console noise only — because the durable file record must not be lost just because the user wanted a clean terminal.

### 6. Default per-namespace levels

```
Default              => Information
IviCli.Backends.*    => Information   (Backend execution traces stay visible)
Microsoft.*          => Warning       (suppress host / DI chatter)
System.Net.*         => Warning
Ivi.Visa.*           => Warning       (NI-VISA SDK noise)
```

Configuration source:

- **Phase 1**: standard `appsettings.json` (`Logging:LogLevel:...`), supplemented by environment variables (`Logging__LogLevel__Default=Debug`). This is the MEL convention and requires no custom integration.
- **Phase 2 / future**: integration with `config.toml`'s `[logging]` section may be added if appsettings duplication becomes a friction point. Not adopted in Phase 1.

### 7. Structured logging conventions

- **Named placeholders only.** `_logger.LogInformation("scan completed devices={Count}", count)`.
- **String interpolation in log messages is prohibited.** `LogInformation($"...")` breaks structured emission. A Roslyn analyzer rule may enforce this in the future; until then it is reviewed at PR time.
- **Value Objects must pass through `ToLogString()`.** `LogInformation("using {Device}", device.ToLogString())`. Alternatively, Serilog's destructuring operator (`@`) is permitted only on types whose every nested member is safe to emit (no embedded secrets); the default expectation is `ToLogString()`.
- **Scopes per invocation.** The composition root opens a single `ILogger.BeginScope(...)` per CLI invocation with at minimum `Command` (e.g. `visa.scan`) and `InvocationId` (short random ID). All subsequent logs inherit these.

### 8. `LoggerMessage` source generation

Adopted **for hot paths only**:

- Every SCPI write / query / read logged by Backend Adapters.
- High-frequency loops inside the gateway (Phase 2).

Hot-path call sites declare partial methods annotated with `[LoggerMessage]`, eliminating boxing and string formatting overhead. Low-frequency logs (CLI handlers, infrastructure startup) use the standard `_logger.LogX(...)` API for readability.

### 9. Tests

- **Default**: `NullLogger<T>` is provided to constructors under test. Tests do not depend on log output unless explicitly verifying it.
- **Verification**: `IviCli.TestKit` provides a `CapturingLogger<T>` that records emitted events for assertion (`logger.ShouldHaveLogged(LogLevel.Warning, contains: "not found")`).
- **xUnit console**: a thin helper in TestKit pipes captured events into `ITestOutputHelper` for diagnostic visibility on test failures.

### 10. Tracing

- **Phase 1: not adopted.** CLI invocations are short-lived and single-purpose; introducing `Activity`-based tracing or correlation IDs has no return on investment at this stage.
- **Phase 2: OpenTelemetry-compatible tracing** is introduced together with the gateway server. Trace and span identifiers are automatically attached to log records (the JSON file sink gains `trace_id` / `span_id` fields). The OpenTelemetry exporter remains optional and disabled by default. The full design lives in ADR 0019.

### 11. Stdout vs stderr / log separation

The CLI's data output (`--json` blobs, human-readable result text) goes to **stdout**; logs go to **stderr** and the file. The two streams must never cross.

- Programmatic consumers can pipe `1>` (data) and `2>` (logs) independently.
- Tests verify that `--json` output on stdout is pure JSON, with no log lines interleaved.

### 12. Sensitive data

Reaffirming ADRs 0014 and 0017:

- VOs use `ToLogString()` to mask hostnames, serial numbers, and other instrument internals.
- `Exception.Message` is logged via `IviError.Cause` to the file sink only. The stderr / stdout user-facing message is constructed from `IviError.Message`, which is template-controlled and free of unmasked data.
- The `--json` payload is governed by the explicit-whitelist rule in ADR 0017 §7; it is not a logging surface.

### 13. Configuration locus

The Serilog logger is constructed only in `IviCli.Cli/Program.cs` (composition root). All other layers consume `ILogger<T>` injected via constructors; they have no knowledge of Serilog, no static loggers, and no service-locator access to `LoggerFactory`.

## Consequences

**Pros**

- Single, well-known backend (Serilog) with a rich Phase 2 upgrade path (OTLP, async sinks, alternative formats).
- Structured logging is the default; AI/CI consumers of the JSON file get well-formed records.
- File and console formats serve their distinct audiences (machines vs humans).
- Stdout / stderr separation keeps `--json` output reliably parseable.
- Tests opt in to logging assertions via `CapturingLogger`; default `NullLogger` keeps tests silent.

**Cons**

- Two output formats to maintain (human console and JSON file).
- Per-namespace level configuration via `appsettings.json` is a second source of truth alongside `config.toml`. Acceptable in Phase 1.
- Source-generated `LoggerMessage` requires explicit partial-method declarations on hot paths; some boilerplate.

**Mitigations**

- Format definitions are centralized in the `Program.cs` Serilog configuration; downstream code remains format-agnostic.
- `appsettings.json` keeps Phase 1 simple by leveraging the MEL standard; consolidation with `config.toml` is revisited only if it becomes a friction point.
- `LoggerMessage` source generation is opt-in per call site; only the highest-frequency loops are converted, keeping the boilerplate proportional to the benefit.
