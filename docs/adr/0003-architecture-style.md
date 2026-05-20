# 0003. Architecture Style

- Status: Accepted
- Date: 2026-05-19

## Context

Assembly layout and dependency direction are established in 0021. This ADR builds on that foundation to determine **code-level architectural discipline**: how far to take DDD, the granularity of CQRS, FP-leaning practices, where DI lives, and the treatment of cross-cutting concerns.

Constraints derived from the PRD and 0021:

- Polymorphism over `IIviBackend` as a transport abstraction (HiSLIP / VXI-11 / Socket / Fake / Replay)
- Separation of the Data Plane (visa query/write/read) and the Control Plane (config / server route / diagnose)
- Separation of static config (`config.toml`) and dynamic session state (`session.json`)
- SCPI itself distinguishes write (`OUTP ON`) from query (`*IDN?`) by syntax

These factors yield "a structure where CQRS naturally fits" and align with "an FP design that pushes side effects to the edge".

## Decision

### 1. Base is Clean Architecture + Hexagonal

- Dependency direction is one-way (per the diagram in 0021).
- **Ports** (interfaces) are defined in the Application layer; each Adapter (Backend / Infrastructure implementation) implements them.
- Domain has zero external dependencies. Application references Domain only.

### 2. Lightweight DDD adoption

Adopted:

- **Vocabulary and distinction between Entity and Value Object**
- **Ubiquitous Language** — names derived from the PRD / Domain Glossary are aligned across code, tests, and logs
- **Anti-Corruption Layer** — raw representations such as VISA resource strings are converted to domain types at the entry of the Application layer; from that point on, only domain types flow through
- **Domain Service** — invariants that do not fit within a single Entity (e.g. ensuring that `defaults.device` exists in `[[devices]]`) are placed as Domain Services

Not adopted (Phase 1):

- **Aggregate Root** — overengineering for the project's scale. The TOML/JSON file as a whole effectively acts as the transaction boundary, so explicit ARs offer little benefit
- **Formalized Repository pattern** — minimal ports such as `IConfigStore` and `ISessionStore` are kept, but `IDeviceRepository` and the like are not introduced
- **Domain Event** — current use cases are synchronous and do not require event-driven flow

If complexity grows, these may be introduced individually via additional ADRs.

### 3. Entity / Value Object classification

The catalog is split out to `docs/domain-glossary.md` (it is expected to grow, so the ADR is not bloated with it).
This ADR records only the classification criteria:

- **Entity**: identity persists independently of attributes (e.g. `Device` remains `psu1` even if `resource` or `timeout_ms` change)
- **Value Object**: equality is determined by the value itself; only replacement applies (e.g. `VisaResource`, `DeviceName`, `Timeout`)
- When in doubt, **prefer VO**. Promote to Entity only once a lifecycle has been observed.

### 4. CQRS — handler separation + read model separation

Adopted scope:

- **Separate Command and Query handlers in the Application layer** (do not unify under a common base).
  - Example: `AddDeviceCommandHandler` / `SetCurrentDeviceCommandHandler` / `ListDevicesQueryHandler` / `GetCurrentDeviceQueryHandler`
- **Separate read models by purpose**:
  - `ConfigDocument` (read-mostly, validation-heavy, human-editable)
  - `SessionState` (write-heavy, volatile is fine, lightweight validation)
  - Do not unify into a single "Repository"
- **Separate methods for write and query on `IIviBackend`**:
  - `WriteAsync(string scpi)` / `QueryAsync<T>(string scpi)` / `ReadAsync<T>()` are separated. Do not merge them through a common `ExecuteAsync` (the `?` suffix distinction of SCPI is lifted to the type level)

Not adopted:

- **Event Sourcing** — excessive for a CLI
- **Dispatch via CommandBus / MediatR** — too much boilerplate for the project's scale. Direct invocation is sufficient (Phase 1)
- **Eventual consistency** — everything completes synchronously

### 5. FP-leaning C# practices

- **Immutable by default**: domain types default to `record`, with changes expressed via `with` expressions
- **Lift failure into the type with a Result type**: business failures use `Result<T, TError>`; only genuine exceptions (disk full, OOM, etc.) are thrown (details in 0014)
- **Dependency Rejection / Impureim Sandwich**: I/O is confined to the edge (Cli handlers, Backend Adapters); the core consists of pure functions
- **DI container only at the composition root**:
  - Only `IviCli.Cli/Program.cs` touches `IServiceCollection`
  - Inside Application / Domain / Backend, dependencies are received as interfaces or `Func<>` via constructor parameters (no reference to the container)
- **Avoid over-generating interfaces**:
  - Only define interfaces where polymorphism is required (`IIviBackend`, `IConfigStore`, `ISessionStore`, `IClock`)
  - For single-implementation components that do not need testability seams, inject the concrete class directly

### 6. Composition Root

`IviCli.Cli/Program.cs` only. Other layers must not reference `IServiceCollection` / `IServiceProvider` (Service Locator is prohibited).

### 7. Cross-cutting concerns (declaration only; details in other ADRs)

| Concern | Policy | Detail ADR |
| --- | --- | --- |
| Logging | Constructor-inject `ILogger<T>` from `Microsoft.Extensions.Logging` | 0011 |
| Validation | Performed at the entry of the Application layer. Inside Domain, enforced at the type level | separate |
| Error handling | Result type first; exceptions only for the exceptional | 0014 |
| Threading | async/await first; Backend requires CancellationToken | 0015 |

## Consequences

**Pros**

- Right-sized for the project's scale (avoids excessive DDD/CQRS/DI ceremonies while preserving the necessary separation of concerns)
- The natural structure of SCPI and config/state can be reflected in the type system
- The entire CLI follows "I/O at the edge, pure core", which is easy to maintain
- Leaves room to introduce Aggregate / Domain Event / Event Sourcing later

**Cons**

- An immutable design based on `record` may be unfamiliar to some C# developers
- The Result type is not in the standard library, so a small in-house implementation is required (or a third-party library is adopted; decided separately)
- Per-handler separation produces many small files

**Mitigations**

- Start with a minimal in-house `Result<T, TError>`; if needed, consider migrating to `OneOf` / `LanguageExt` later
- The number of files is offset by the symmetric src/tests layout (0021), which preserves navigability
- Naming and placement patterns are covered by `docs/domain-glossary.md` and this ADR
