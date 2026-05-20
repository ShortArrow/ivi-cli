# 0021. Repository Layout

- Status: Accepted
- Date: 2026-05-19

## Context

Before starting Phase 1, the assembly split and directory layout must be finalized.
The PRD positions `IIviBackend` as a transport abstraction, and HiSLIP / VXI-11 / Socket differ significantly in their NuGet dependencies and implementation complexity.
A split that naturally expresses both Clean Architecture (unidirectional dependency direction toward higher-level abstractions, with no reverse references or cycles) and Backend polymorphism is required.

## Decision

### Top-level directory

Adopt the .NET standard `src/` + `tests/`.

```
ivi-cli/
 ├─ src/
 ├─ tests/
 ├─ docs/
 ├─ build/         # Shared MSBuild settings such as Directory.Build.props (future)
 └─ ivi-cli.slnx
```

### Assembly split

Express both the CA layers and Backend polymorphism.

**To be materialized in Phase 1:**

| Assembly | Role |
| --- | --- |
| `IviCli.Domain` | Entities and value objects (Device, Alias, VisaResource, …). No external dependencies |
| `IviCli.Application` | Use cases and port interfaces (`IIviBackend`, etc.). References Domain only |
| `IviCli.Infrastructure` | Technical details such as config.toml / session.json persistence and time provider |
| `IviCli.Backends.Local` | Local backend via NI-VISA / IVI |
| `IviCli.Backends.Fake` | In-memory backend used in tests and CI |
| `IviCli.Cli` | System.CommandLine-based entry point (composition root) |

**To be materialized in Phase 2 (this ADR only reserves slnx slots; generation is deferred):**

| Assembly | Role |
| --- | --- |
| `IviCli.Backends.HiSlip` | HiSLIP backend |
| `IviCli.Backends.Vxi11` | VXI-11 backend |
| `IviCli.Backends.Socket` | Raw TCP socket backend |
| `IviCli.Backends.Replay` | Session recording / replay backend |
| `IviCli.Server` | Remote instrument gateway |
| `IviCli.Management` | gRPC / HTTP management API |

### Dependency direction

```
Domain ← Application ← Infrastructure
                    ↑
                    Backends.*    (implement Application's ports)
                    ↑
                    Cli (composition root)

Future: Server / Management → Application → Domain
```

- Dependencies always point toward the higher-level (abstract) layer. Reverse references and cycles are forbidden.
- Ports such as `IIviBackend` are defined in `IviCli.Application`, and each `IviCli.Backends.*` implements them.
- Only `IviCli.Cli` may reference all layers (composition root for DI wiring).

### Test project mapping

Mirror `.Tests` projects under `tests/` with the same names as their `src` counterparts.
Integration tests that depend on physical hardware are isolated via `[Trait("Category","Integration")]` (see 0009-testing-strategy for details).

```
tests/
 ├─ IviCli.Domain.Tests/
 ├─ IviCli.Application.Tests/
 ├─ IviCli.Infrastructure.Tests/
 ├─ IviCli.Backends.Local.Tests/   # Primarily integration tests
 ├─ IviCli.Backends.Fake.Tests/    # Behavior tests for Fake itself
 └─ IviCli.Cli.Tests/
```

### Naming

- Place `Backends` in the middle segment, as in `IviCli.Backends.HiSlip`. Do not use `IviCli.Infrastructure.Backends.HiSlip` (verbose, and it weakens the intent that Backends are independently swappable).
- Keep assembly name = root namespace = csproj filename identical.
- Use the same name for the directory as well (`src/IviCli.Backends.HiSlip/IviCli.Backends.HiSlip.csproj`).

## Consequences

**Pros**

- Heavy NuGet dependencies for HiSLIP / VXI-11 etc. do not propagate into the `IviCli.Cli` core (Backends are swapped plugin-style via DI).
- CA dependency-direction violations are mechanically detectable via `dotnet list reference`.
- Test project assignment is mechanically determined (1:1 with src).

**Cons**

- Many initial `dotnet new` invocations (Phase 1: src 6 + tests 6 = 12).
- Even small changes may span multiple projects.

**Mitigations**

- Centralize `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, etc. in `build/Directory.Build.props` (to be decided in a separate ADR or when scaffolding).
- Keep assembly naming and dependency direction consistent with 0023 (FP), 0009 (TDD), and 0017 (Security).
