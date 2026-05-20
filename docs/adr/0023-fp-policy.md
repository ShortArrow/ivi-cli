# 0023. Functional Programming Policy

- Status: Accepted
- Date: 2026-05-20

## Context

ADR 0003 adopted "FP-leaning C#", but stayed at the declarative level. This ADR establishes **the concrete conventions needed to translate that stance into code**: how strictly to enforce immutability, where the Result type comes from, whether to introduce Option, how to express sum types, the async policy, and where the pure/impure boundary sits.

C# is an OO language, and pursuing FP to its full extent would mean fighting the language. The practical goal is to **push side effects to the edges and keep the core pure**, within the limits of readability.

## Decision

### 1. Immutability

- **Domain types are `record`** (positional records preferred).
- Public properties may only have `init`. `set` is forbidden.
- When a collection is exposed across a domain boundary, return `IReadOnlyList<T>`, `IReadOnlyDictionary<TKey,TValue>`, or `ImmutableArray<T>`. Do not expose `List<T>` or `Dictionary<,>`.
- Mutable collections may be used in internal implementations, but must be wrapped as read-only before they reach the caller.
- Mutation is expressed by constructing a new instance with the `with` expression.

Example:

```csharp
public sealed record Device(DeviceName Name, VisaResource Resource, Timeout Timeout);

var updated = device with { Timeout = Timeout.FromMilliseconds(5000) };
```

### 2. Nullable Reference Types (NRT)

- `<Nullable>enable</Nullable>` across all projects (configured centrally in `build/Directory.Build.props`, and added in the project template).
- Warnings are treated as errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is used alongside; the exact scope is to be pinned down in a separate ADR).
- **`null!` is forbidden**. Any unavoidable escape hatch (framework requirements, etc.) must carry a `// nullable-escape: <reason>` comment and be allowed case by case.
- "Absence" is expressed with `T?`, consistent with §3 below.

### 3. Do not introduce Option<T>

- C#'s NRT (`T?`) is sufficient. `Option<T>` libraries (`LanguageExt` and the like) are not adopted.
- When expressiveness in a pipeline is lacking, supplement `T?` with extension methods (in-house `.Map`, `.Bind`, etc.).
- Rationale: combining strict NRT with Option leads to double representation and confuses reviewers. We standardize on NRT alone.

### 4. Result<T, TError> — minimal in-house implementation

A minimal implementation lives inside `IviCli.Domain`. No library dependency.

Intended shape:

```csharp
public abstract record Result<T, TError>
{
    public sealed record Ok(T Value) : Result<T, TError>;
    public sealed record Error(TError Err) : Result<T, TError>;
}

public static class Result
{
    public static Result<T, TError> Success<T, TError>(T value) => ...;
    public static Result<T, TError> Failure<T, TError>(TError err) => ...;
}

public static class ResultExtensions
{
    public static Result<U, TError> Map<T, U, TError>(this Result<T, TError> r, Func<T, U> f);
    public static Result<U, TError> Bind<T, U, TError>(this Result<T, TError> r, Func<T, Result<U, TError>> f);
    public static Result<T, FError> MapError<T, TError, FError>(this Result<T, TError> r, Func<TError, FError> f);
    public static R Match<T, TError, R>(this Result<T, TError> r, Func<T, R> ok, Func<TError, R> err);
}
```

The final API will be settled at implementation time. Because any library migration is contained within `IviCli.Domain`, a future switch to `OneOf` or `LanguageExt` remains local.

### 5. Sum types as a sealed record hierarchy

Because C# has no native discriminated union, a closed type set is expressed with `abstract record` + `sealed record`.

```csharp
public abstract record VisaResource;
public sealed record Tcpip(Host Host, string Board, string Suffix) : VisaResource;
public sealed record Usb(string VendorId, string ProductId, string Serial) : VisaResource;
public sealed record Gpib(int Board, int PrimaryAddress) : VisaResource;

string Describe(VisaResource r) => r switch
{
    Tcpip t => $"TCPIP {t.Host}",
    Usb u   => $"USB {u.VendorId}:{u.ProductId}",
    Gpib g  => $"GPIB::{g.PrimaryAddress}",
};
```

- Use the `switch` expression to enforce exhaustiveness. Adding a new case should trigger compiler warnings across all switches (a pseudo-`exhaustive` regime).
- Do not add an `enum Kind` discriminator property (the type itself is the discriminator).

### 6. Pure / impure boundary — Impureim Sandwich

```
[Impure] Read inputs   →  Cli handler / Backend / Infrastructure (I/O)
[Pure]   Compute       →  Application / Domain (no side effects)
[Impure] Write outputs →  Cli (stdout/stderr/exit code) / Backend / Infrastructure
```

- The Domain layer forbids side effects (no I/O, no clock reads, no randomness, no exception throws either).
- The Application layer expresses I/O only via ports (never calls it directly).
- Methods that involve I/O must carry the `*Async` suffix (pure computations are synchronous methods).
- Side-effecting ports such as `IClock` and `IRandom` are explicitly injected at the Application layer (so they can be swapped in tests).

### 7. Async policy

- `async/await` is adopted across the board. Do not mix sync and async (synchronous wrappers via `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` are forbidden).
- Public async methods take `CancellationToken` as a **required argument** (no default value either — the caller must pass it explicitly).
- `ConfigureAwait(false)`: this project ships only as a CLI binary and has no SynchronizationContext-induced deadlock risk, so **we do not write it**. To be revisited if and when the code is repackaged as a library.
- `async void` is forbidden (when handlers etc. seem to require it, build an `await`-able entry point rather than blocking with a synchronous wrap).

### 8. Interface vs Func / delegate

- When polymorphism is needed or multiple methods are involved → **interface** (`IIviBackend`, `IConfigStore`, `ISessionStore`, `IClock`).
- When a port can be expressed by a single function → injectable as **`Func<...>` / delegate**.
- Do not proliferate interfaces solely for testing. If concrete classes plus directly-passed Fakes read better in tests, skip the interface.

### 9. Pattern matching

- Prefer the `switch` **expression** over the `switch` **statement**.
- Make use of `is` patterns, property patterns, relational patterns, and list patterns.
- Expression-bodied members are limited to side-effect-free methods.

### 10. LINQ policy

- Use LINQ chains when they make the expression clearly more readable.
- On hot paths (paths that every command traverses, inner loops inside Backend, etc.), `foreach` is acceptable. Where allocations should be avoided, do not force LINQ (pragmatism).
- Avoid usages that break purity, such as mutating after `.ToList()` with side effects.

### 11. Handling exceptions (consistent with 0014)

- Business failures (validation, parse, not found, conflict, transport error) are represented as **Result.Error**.
- Exceptions are thrown for:
  - Programming errors (precondition violations, `ArgumentNullException`, etc.).
  - Genuinely exceptional conditions (OOM, disk full, OS exception propagation).
- `catch` is permitted only at the outermost ring of the composition root (`IviCli.Cli/Program.cs`). Catching at lower layers is allowed solely "to repackage into a Result".

The details will be settled separately in 0014.

## Consequences

**Pros**

- The core is pure and testable; tests that substitute I/O with Fakes/Mocks are easy to write (consistent with the premises of 0009).
- The sealed record hierarchy plus `switch` expression yields type-level exhaustiveness.
- The NRT + Result pairing pins down a single way to express failure.
- FP expression is complete with zero library dependencies (no extra NuGet dependency in production binaries).

**Cons**

- Additional cost of maintaining an in-house Result implementation (around 100 lines) and the responsibility that comes with it.
- Some C# developers are unfamiliar with records, pattern matching, or Result.
- Strict NRT can produce warning-suppression busywork at external-library boundaries.

**Mitigations**

- Start the Result API minimal and extend as needed. A future switch to `OneOf` or `LanguageExt` remains local within `IviCli.Domain`.
- The unfamiliarity issue is absorbed by `docs/domain-glossary.md`, this ADR, and consistency in the project template code.
- NRT warning suppression is kept trackable via `// nullable-escape: <reason>`.
