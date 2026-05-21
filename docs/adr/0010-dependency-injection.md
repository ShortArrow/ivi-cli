# 0010. Dependency Injection

- Status: Accepted
- Date: 2026-05-21

## Context

ADR 0003 placed DI at the composition root only and discouraged the over-generation of interfaces. ADR 0023 reinforced the FP-leaning baseline: prefer pure functions, immutable records, and `Func<>` over interface ceremony when polymorphism is not required. This ADR pins down the concrete DI rules: which container, what goes inside it, lifetimes, registration patterns, the boundary between FP-style direct composition and container-managed wiring, and the IO-mocking strategy that flows from those choices.

The goal is to keep the FP shape of the code as far as it scales and to fall back to DI only where the container's benefits genuinely exceed its cost.

## Decision

### 1. Container: Microsoft.Extensions.DependencyInjection

`Microsoft.Extensions.DependencyInjection` is the sole DI container. Autofac, Scrutor, and other libraries are not adopted in Phase 1; explicit registration is feasible at this scale (fewer than 30 services). Reassessment is deferred to the point where registration count or wiring complexity becomes a real friction.

### 2. What goes in the container, what stays out

**In the container** (DI-managed):

- Ports with multiple implementations: `IIviBackend` and its `IBackendFactory`.
- Framework-crossing services: `ILogger<T>` (via Serilog bridge per ADR 0011), `IClock`, `IRandom` (when needed).
- I/O adapters expressed as ports: `IConfigStore`, `ISessionStore`.
- Use-case handlers: `AddDeviceCommandHandler`, `ListDevicesQueryHandler`, etc. — single implementation each, but injecting them keeps Cli-level wiring uniform.
- Domain Services with cross-entity validation: `ConfigValidator`, `AliasResolver` — registered as concretes (no extra interface, see §5).
- The root pipeline / command dispatcher: `IRootPipeline`.
- Loaded configuration POCOs: `ConfigDocument` (see §7).

**Outside the container** (composed directly, FP-style):

- Domain Entities and Value Objects.
- Pure functions and static factories (`VisaResource.Parse`, `IdnResponse.Parse`, etc.).
- `Result<T, TError>` and the per-domain error sum types.
- Test fixtures and Test Data Builders.
- Adapter-internal pure logic (e.g. `HislipFrame.Encode/Decode`, `TomlConfigParser.Parse`).

### 3. Lifetimes

Phase 1 is effectively **singleton-only**:

- A CLI invocation is a single process; scoped and transient lifetimes add ceremony without value.
- All ports, handlers, validators, factories, and loaded configuration objects register as `Singleton`.

`Scoped` may be reintroduced in Phase 2 when the gateway server hosts per-request work. `Transient` is avoided unless a service truly cannot be safely shared (e.g. wrapping `Activity` or `IDisposable` per use).

### 4. Backend selection: `IBackendFactory`

`IIviBackend` has multiple implementations, and the choice depends on the device's resource string. Backend resolution goes through a factory.

```csharp
public interface IBackendFactory
{
    Result<IIviBackend, BackendError> CreateFor(Device device);
}
```

- `DefaultBackendFactory` (in `IviCli.Infrastructure`) inspects the device's `VisaResource` and dispatches to the matching Backend (`Local`, `HiSlip`, `Socket`, `Fake`, `Replay`).
- Each Backend assembly registers its own concrete `IIviBackend` instance into the container; the factory composes them.
- Tests substitute the factory or substitute individual `IIviBackend` registrations as needed (see §9).

Keyed services (MEL 8.0+) are not used in Phase 1 to keep the factory's selection logic explicit and easy to read.

### 5. `Func<>` and delegate vs interface

| Situation | Choice |
| --- | --- |
| Multiple implementations needed (real polymorphism) | **interface** |
| Multiple methods that conceptually belong to the same abstraction (Read / Write / Save) | **interface** |
| Cross-cutting concern with framework convention (`ILogger<T>`) | **interface** |
| Single-method port that maps a value to a value | **`Func<>` / delegate** |
| Handler-local helper | **local function / lambda** |
| Container-resident service | usually **interface or concrete class** |

The practical rule: **what lives in the container is interface or concrete; what is passed ad-hoc between functions is `Func<>` or a delegate**.

Single-implementation services (`ConfigValidator`, `AliasResolver`, `*Handler`, `ExitCodeMapper`) are registered as **concrete classes** without an extra interface — they have no polymorphism need and the absence of an interface keeps the surface honest. Tests use the real class.

### 6. Registration: per-assembly extension methods

Each assembly that contributes services exposes a single `IServiceCollection` extension method. The composition root chains them.

```csharp
// IviCli.Application
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddIviCliApplication(this IServiceCollection s) => s
        .AddSingleton<ConfigValidator>()
        .AddSingleton<AliasResolver>()
        .AddSingleton<AddDeviceCommandHandler>()
        .AddSingleton<ListDevicesQueryHandler>()
        // ...
        ;
}

// IviCli.Cli/Program.cs (composition root)
services
    .AddIviCliApplication()
    .AddIviCliInfrastructure(config)
    .AddIviCliBackendsLocal()
    .AddIviCliBackendsFake()
    .AddIviCliCli();
```

- Each extension lives in `<AssemblyName>.ServiceCollectionExtensions`.
- Assembly scanning (e.g. via Scrutor) is not used. Explicit registration is faster to read at this scale and never magic-fails at runtime.
- Per-assembly extensions encapsulate which dependencies each layer actually needs; the composition root reads top-to-bottom like a manifest.

### 7. Configuration objects: immutable POCO singletons

- At startup, `IConfigStore.LoadAsync()` returns a `ConfigDocument` record. The composition root registers it: `services.AddSingleton(configDocument)`.
- Downstream code consumes `ConfigDocument` directly via constructor injection. No `IOptions<T>` wrapper.
- `IOptions<T>` / `IOptionsSnapshot<T>` / `IOptionsMonitor<T>` are deferred to a future ADR if hot-reload becomes a requirement.

This matches ADR 0023's preference for immutable records and avoids the indirection of MEL's options pattern when no reloading is required.

### 8. Service Locator: prohibited outside the composition root

- `IServiceProvider` is referenced **only** inside `IviCli.Cli/Program.cs` and inside `Add*` extension methods (where factory lambdas may capture it).
- Application, Domain, and Backend code receives dependencies through constructors. Calling `sp.GetRequiredService<T>()` from these layers is forbidden.
- Factory delegate registrations such as `services.AddSingleton<Func<X, Y>>(sp => x => sp.GetRequiredService<Z>().Do(x))` are acceptable at the composition root because they evaluate within the registration scope.

### 9. IO mocking strategy

Mocking happens at the **abstraction level appropriate to the test's focus**, not uniformly at the raw IO layer.

#### 9.1 File system

```
[High]  IConfigStore / ISessionStore  ← port (domain concept)
[Mid]   IFileSystem (System.IO.Abstractions) ← adapter-internal IO abstraction
[Low]   System.IO primitives
```

| Test target | Mock layer | Notes |
| --- | --- | --- |
| Application / Use-case handler | `IConfigStore` fake (in-memory) | Domain-level. `FakeConfigStore` built via `ConfigBuilder` (TestKit). |
| Adapter unit test (`TomlConfigStoreTests`) | `IFileSystem` (`MockFileSystem`) | Verifies parse/serialize and atomic-write logic without touching disk. |
| Adapter integration test | Real filesystem in temp directory | `[Trait("Category","Integration")]`. Validates actual `chmod` / NTFS ACL / atomic rename. |

Adapter internals follow the Impureim Sandwich (ADR 0023): IO via `IFileSystem`, then pure `TomlConfigParser.Parse(string) => Result<ConfigDocument, ConfigError>` for the interesting logic. The parser is independently unit-testable as a pure function.

#### 9.2 VISA

```
[High]  IIviBackend                              ← port (domain operation)
[Mid]   IVisaTransport / IHislipFramer (per Backend, when worthwhile)
[Low]   Ivi.Visa.* / System.Net.Sockets
```

| Test target | Mock layer | Notes |
| --- | --- | --- |
| Application / handler | `FakeBackend` | The primary substitute. Covers the vast majority of Application-level scenarios via the schedule DSL declared in ADR 0009. |
| `HislipFrame` parser unit test | None (pure function) | `HislipFrame.Parse(bytes) => Result<HislipFrame, ParseError>` is a pure function inside `IviCli.Backends.HiSlip`. |
| `HiSlipBackend` transport state machine | `INetworkStream` mock | Substitutes socket reads/writes without binding a real port. |
| `SocketBackend` | `INetworkStream` mock | Same shape, simpler protocol. |
| `LocalVisaBackend` (NI-VISA SDK wrap) | None at unit level | The wrap is mostly transparent; only `VisaExceptionMapper.Map(Exception) => BackendError` is unit-tested as a pure function. Full behavior is integration-tested with real NI-VISA. |
| All Backends, end-to-end | Real instrument or simulator | `[Trait("Category","Integration")]`. |

Adapter-internal abstractions (`INetworkStream`, `IHislipFramer`) live **inside the Backend assembly** as `internal` types. They are not registered in the DI container and do not appear in the public surface. The Backend's public `IIviBackend` implementation `new`s them or receives them via internal constructors used by tests.

The key contrast with file IO: because `IIviBackend` itself is a wide enough abstraction, the Fake covers most of the substitution work that for file IO falls to `IFileSystem`. Intermediate abstractions are introduced only when a Backend has nontrivial separable logic (HiSLIP framing, socket framing) — never for adapters whose role is a thin SDK wrap.

### 10. Phase 1 registration catalog

```
[Singleton] ILogger<T>            <- Serilog (MEL bridge), via AddSerilog(...)
[Singleton] IClock                <- SystemClock
[Singleton] IFileSystem           <- new FileSystem() from System.IO.Abstractions
[Singleton] IConfigStore          <- TomlConfigStore
[Singleton] ISessionStore         <- JsonSessionStore
[Singleton] IBackendFactory       <- DefaultBackendFactory
[Singleton] IIviBackend (local)   <- LocalVisaBackend         (registered by AddIviCliBackendsLocal)
[Singleton] IIviBackend (fake)    <- FakeBackend              (registered by AddIviCliBackendsFake)
[Singleton] ConfigDocument        <- loaded at startup
[Singleton] ConfigValidator       <- concrete
[Singleton] AliasResolver         <- concrete
[Singleton] AddDeviceCommandHandler / ListDevicesQueryHandler / ... <- concretes
[Singleton] IRootPipeline         <- DefaultRootPipeline
[Singleton] ExitCodeMapper        <- concrete
```

### 11. Partial application as an alternative to class injection

For cases where a dependency reduces to "this config, given this value, returns that result", partial application via `Func<>` is acceptable in place of a class.

```csharp
// IviCli.Domain
public static class AliasResolver
{
    public static Result<DeviceName, ResolveError> Resolve(ConfigDocument config, string raw) { ... }
}

// composition root
services.AddSingleton<Func<string, Result<DeviceName, ResolveError>>>(sp =>
    raw => AliasResolver.Resolve(sp.GetRequiredService<ConfigDocument>(), raw));

// handler
public sealed class SomeHandler(Func<string, Result<DeviceName, ResolveError>> resolveAlias)
{
    ...
}
```

Either style is permitted. Class injection is the default. Partial application is preferred when the dependency is a single function and the consumer reads more naturally with a `Func<>` parameter than a one-method class.

### 12. Test substitution

`IviCli.TestKit` provides a `TestHostBuilder` that constructs an `IServiceProvider` mirroring the production composition with selected overrides:

```csharp
var sp = TestHostBuilder.Build(overrides => overrides
    .Replace<IIviBackend>(new FakeBackend(scenario))
    .Replace<IClock>(new FixedClock("2026-05-21Z"))
    .Replace<IConfigStore>(new FakeConfigStore(initialConfig)));
```

The default composition uses `FakeBackend` and `FakeConfigStore`, so most tests pass minimal overrides. Reaches into the real filesystem or real NI-VISA SDK happen only in integration tests, which use explicit registrations to opt in to real adapters.

## Consequences

**Pros**

- The container's responsibility is narrow and explicit; reading `Program.cs` yields a complete inventory of moving parts.
- Pure functions and Value Objects stay out of the container, preserving FP composition where it adds value.
- Backend polymorphism is expressed as a single factory port, keeping the rest of the code Backend-agnostic.
- IO mocking decisions are framed by test focus, not by uniform layer rule, so each test reaches for the cheapest substitute.
- Singleton-only Phase 1 sidesteps lifetime bugs.

**Cons**

- Manual `Add*` extension methods must be updated when new services are introduced; assembly scanning would be more "automatic" at the cost of opacity.
- Concrete-class registration (no interface) requires the test to override the concrete type, which is slightly less idiomatic than overriding an interface.
- Partial application via `Func<>` and class injection are both permitted; reviewers must enforce taste.

**Mitigations**

- The extension-method registration is colocated with the assembly that owns the service; adding a service is a local change.
- `TestHostBuilder` supports `Replace<T>` on concrete types just as on interfaces; the asymmetry is purely stylistic.
- The default is "class injection". `Func<>` is a deliberate choice flagged in code review when applied.
