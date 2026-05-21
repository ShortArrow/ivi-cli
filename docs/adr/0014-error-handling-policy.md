# 0014. Error Handling Policy

- Status: Accepted
- Date: 2026-05-21

## Context

ADR 0023 declared the FP-leaning baseline — `Result<T, TError>` for business failures, exceptions reserved for truly exceptional conditions, with `catch` confined to the composition root. ADR 0017 added input-validation expectations and log-masking via `ToLogString()`. This ADR fills in the concrete rules: the shape of error types, validation aggregation, cancellation, exit codes, layer-boundary mapping, and — importantly — how errors connect to the logging subsystem so that diagnostic information is preserved without leaking sensitive content.

Logger configuration itself (sinks, file rotation, format) belongs to ADR 0011. This ADR defines only the *interface* between errors and the logger.

## Decision

### 1. Error type hierarchy: per-domain sealed sum types

Each layer defines its own error sum type as `abstract record` + `sealed record` variants, consistent with the sum-type pattern in ADR 0023.

```csharp
public abstract record DeviceError : IviError;
public sealed record DuplicateDeviceName(DeviceName Name) : DeviceError;
public sealed record DeviceNotFound(DeviceName Name) : DeviceError;
public sealed record InvalidDeviceNameFormat(string Raw) : DeviceError;

public abstract record BackendError : IviError;
public sealed record TransportTimeout(TimeSpan Elapsed, Exception? Cause = null) : BackendError;
public sealed record TransportDisconnected(string Reason, Exception? Cause = null) : BackendError;
public sealed record DeviceNotResponding(VisaResource Resource) : BackendError;

public abstract record ConfigError : IviError;
public sealed record DefaultDeviceMissing(DeviceName Name) : ConfigError;
public sealed record DefaultServerMissing(ServerName Name) : ConfigError;
public sealed record DuplicateName(string Kind, string Name) : ConfigError;
```

`IviError` is a marker interface implemented by every error type. It exposes the contract required by the logging integration (§9). It does **not** force a common discriminator field — `switch` over the concrete sum hierarchy remains the primary form of dispatch.

The catalog of error types lives next to the owning domain in code; this ADR shows representative examples only.

### 2. Validation aggregation

- Validators that perform cross-entity checks (`ConfigValidator`, multi-field input validators) return `Result<T, IReadOnlyList<TError>>` to surface every violation in one pass.
- Single-purpose validators (Value Object factories like `DeviceName.From`) return `Result<T, TError>` with a single error. Multiple violations on a single value are not meaningful at this granularity.

### 3. Cancellation

`OperationCanceledException` is **not** converted to a `Result.Error`. It propagates as an exception and is caught only at the composition root, which exits with code `130` (POSIX-style SIGINT convention).

Rationale: cancellation is user-initiated or timeout-initiated, not a business failure. Treating it as part of every Result type bloats every method signature and conflates two distinct concepts.

### 4. Exit code mapping

A dedicated mapper in the Cli layer translates `IviError` instances to POSIX-style exit codes.

| Class | Exit code |
| --- | --- |
| Success | `0` |
| Generic failure | `1` |
| Usage error (CLI parse, argument validation) | `2` |
| Transport error (`BackendError`) | `3` |
| Configuration error (`ConfigError`) | `4` |
| Device / domain error (`DeviceError`, etc.) | `5` |
| Cancelled | `130` |
| Unhandled exception | `1` (with critical log) |

The mapping is implemented as a single switch expression in `IviCli.Cli` (e.g. `ExitCodeMapper.Map(IviError)`). Adding new categories updates the table here and the switch in lockstep.

### 5. Layer-boundary error mapping

Each layer mapping is **explicit**. Lower-layer errors do not propagate verbatim; they are wrapped or converted at the boundary using `.MapError(...)`.

```csharp
// Application layer wraps a BackendError into an ApplicationError variant.
Result<IdnResponse, ApplicationError> result =
    await _backend.QueryAsync(device, ScpiQuery.Idn, ct)
        .MapError(be => new ApplicationError.BackendFailure(be));
```

This preserves the lower-layer error as data while letting upper layers reason in terms of their own vocabulary.

### 6. When to throw vs when to return `Result.Error`

**Throw:**

- Caller precondition violations: `ArgumentNullException`, `ArgumentOutOfRangeException`
- Programmer errors: `InvalidOperationException`, `ObjectDisposedException`
- Genuine exceptional conditions: `OutOfMemoryException`, OS-originated I/O errors that the program cannot meaningfully recover from
- Cancellation: `OperationCanceledException` propagates uncaught

**Return `Result.Error`:**

- Validation failures
- Not-found / conflict / duplicate
- Transport timeouts and disconnections (converted by Backend Adapters, see §7)
- Domain rule violations

### 7. Backend Adapter: Exception → Result conversion

Each Backend Adapter (`IviCli.Backends.*`) wraps its outermost call boundary in a `try`/`catch` and converts a **known set** of transport exceptions into `BackendError` variants. Unknown exceptions are rethrown.

Known list (representative; the actual list lives in the adapter):

- `TimeoutException` → `TransportTimeout`
- `SocketException`, `IOException` (from a network stream) → `TransportDisconnected`
- VISA SDK-specific exceptions (e.g. `Ivi.Visa.NativeVisaException`, when wrapping NI-VISA) → an enumerated `BackendError` variant
- `OperationCanceledException` is **never** caught here; it propagates

`Cause: Exception?` on the resulting error variant preserves the original exception for diagnostic logging (§9), but it is never surfaced to user-facing output.

### 8. `Result<T, TError>` API (recap from ADR 0023)

```csharp
public abstract record Result<T, TError>
{
    public sealed record Ok(T Value)   : Result<T, TError>;
    public sealed record Error(TError Err) : Result<T, TError>;
}

public static class Result
{
    public static Result<T, TError> Success<T, TError>(T value);
    public static Result<T, TError> Failure<T, TError>(TError err);
    public static Result<T, TError> Try<T, TError>(Func<T> body, Func<Exception, TError> onError);
    public static async Task<Result<T, TError>> TryAsync<T, TError>(
        Func<CancellationToken, Task<T>> body,
        Func<Exception, TError> onError,
        CancellationToken ct);
}

public static class ResultExtensions
{
    public static Result<U, TError> Map<T, U, TError>(this Result<T, TError> r, Func<T, U> f);
    public static Result<U, TError> Bind<T, U, TError>(this Result<T, TError> r, Func<T, Result<U, TError>> f);
    public static Result<T, FError> MapError<T, TError, FError>(this Result<T, TError> r, Func<TError, FError> f);
    public static R Match<T, TError, R>(this Result<T, TError> r, Func<T, R> ok, Func<TError, R> err);
}
```

`Result.Try` / `Result.TryAsync` are the canonical bridges from legacy throwing APIs into `Result`. They never swallow `OperationCanceledException`.

### 9. Error ↔ logging contract

Errors are pure data; **logging happens at a single point — the composition root**. Lower layers neither log nor partially handle errors before returning them.

The `IviError` marker exposes the contract used by the composition-root logger:

```csharp
public interface IviError
{
    LogLevel Level { get; }                  // default per category, overridable per variant
    string Message { get; }                  // user-facing English description (no PII)
    IReadOnlyList<object?> LogArgs => Array.Empty<object?>();  // structured-logging placeholders
    Exception? Cause => null;                // optional inner exception, for diagnostics only
}
```

At the composition root:

```csharp
_logger.Log(error.Level, error.Cause, error.Message, error.LogArgs.ToArray());
```

Conventions:

- **`Message`** is a constant or templated English string with `{Placeholder}` markers consumed by structured logging. It must not embed unmasked sensitive data; embed VOs and let `ToLogString()` do the masking.
- **`LogArgs`** carries the placeholder values. VOs implementing `ToLogString()` (ADR 0017) are emitted via that method when the logger invokes their `ToString()` substitute.
- **`Cause`** is included for adapter-converted exceptions so the original stack trace is captured in structured logs; it is never echoed to stdout / stderr.
- **Default `Level`**: `DeviceError` / `ConfigError` → `Warning`; `BackendError` → `Error`; `Unknown` / uncaught → `Critical`. Individual variants may override.

Single logging point implies **no `LogError` calls in handlers, adapters, or domain code**. Adapters log nothing on success; on failure they return a `Result.Error` whose `Cause` carries the original exception.

The only exceptions to "log only at composition root" are:

- An adapter catching an unknown exception and rethrowing: it may log at `Critical` immediately before rethrowing, since context may be lost as the exception bubbles.
- Test diagnostics: `ITestOutputHelper`-level logs in test code are exempt.

### 10. Error message language

All error `Message` strings are English (per ADR 0024). Localization is deferred; if introduced, it will be via resource files and is a separate ADR.

### 11. Composition-root pattern (illustrative)

```csharp
// IviCli.Cli/Program.cs
public static async Task<int> Main(string[] args)
{
    using var host = HostBuilder.Build(args);
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var pipeline = host.Services.GetRequiredService<IRootPipeline>();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    try
    {
        var result = await pipeline.RunAsync(args, cts.Token);
        return result.Match(
            ok:  _ => 0,
            err: e =>
            {
                logger.Log(e.Level, e.Cause, e.Message, e.LogArgs.ToArray());
                Console.Error.WriteLine(FormatUserMessage(e));
                return ExitCodeMapper.Map(e);
            });
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("cancelled");
        return 130;
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Unhandled exception at composition root");
        Console.Error.WriteLine($"fatal: {ex.Message}");
        return 1;
    }
}
```

`FormatUserMessage` renders the error in a form suitable for stdout/stderr (no `Cause`, no internal placeholders); the logger receives the full structured payload.

### 12. Correlation ID / tracing

Phase 1 does not introduce correlation IDs or `Activity`-based tracing. CLI invocations are short-lived and single-purpose; the cost of plumbing outweighs the benefit. Phase 2 (gateway server) revisits this through ADR 0011 / 0019.

## Consequences

**Pros**

- Errors are data; testable in pure form, independent of logger state.
- Single logging point eliminates double-logging and centralizes log-level policy.
- `Cause` preserves diagnostic stack traces without leaking them to users.
- VO-level `ToLogString()` masking (ADR 0017) extends naturally through structured logging.
- Exit-code policy is one switch expression — easy to audit.

**Cons**

- Every error type implementing the `IviError` contract is small boilerplate.
- Explicit `.MapError` at every layer boundary is more verbose than letting errors propagate untyped.
- Cancellation as exception (not Result) is a special-case carved out of an otherwise uniform error model.

**Mitigations**

- A small set of base records (`abstract record DeviceError : IviError` etc.) handles the boilerplate per layer.
- `.MapError` is colocated with handler logic and reads as a deliberate boundary — the verbosity is a feature, not noise.
- The cancellation rule is documented here and reinforced by tests in `IviCli.Cli.Tests`.
