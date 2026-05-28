# 0037. Real-hardware integration test gating

- Status: Accepted
- Date: 2026-05-28

## Context

Batch C ([ADR 0009 §3](0009-testing-strategy.md)) introduced
`[Requires("python","pyvisa")]` to gate PyVISA-driven interop tests:
each prerequisite probes once per test process via `PrereqProbe`,
caches the result, and missing prerequisites set
`FactAttribute.Skip` to a precise reason string. The PyVISA tests
hit the HiSLIP and VXI-11 gateway implementations, but they speak
the wire protocols — they don't exercise the **Local backend's
reflection plumbing** against an actual installed IVI Shared
Components runtime (`Ivi.Visa.dll`, distributed via NI-VISA or
Keysight VISA, not via nuget.org).

`ReflectionVisaSessionFactory`
(`src/IviCli.Backends.Local/ReflectionVisaSessionFactory.cs`)
lazily resolves `Ivi.Visa.GlobalResourceManager` and
`Ivi.Visa.IMessageBasedSession` at first use; CI without the runtime
correctly falls into the `LocalVisaRuntimeMissing` branch. What was
missing is a positive-side test: when the runtime IS present, does
the reflection plumbing actually reach `GlobalResourceManager.Open`
without raising? A regression here (renamed type, wrong overload,
missing binding flag) would today only surface on an operator's
machine — far too late.

## Decision

### 1. Probe strategy

`PrereqProbe` gains a `"ni-visa"` case backed by
`Lazy<bool>` caching the result of `Assembly.Load("Ivi.Visa")`:

```csharp
private static readonly Lazy<bool> _niVisa = new(() =>
{
    try { return Assembly.Load("Ivi.Visa") is not null; }
    catch { return false; }
});
```

- True when the IVI shared components are present on the process's
  assembly resolution path (typically `%WINDIR%\Microsoft.NET\assembly`
  on Windows after an NI-VISA / Keysight VISA install).
- Cached once per test process so repeated gate evaluations stay
  cheap.
- The probe never tries to **open** a session — that is the test's
  job, and the test must tolerate the lack of any listening
  instrument by surfacing a `BackendError` rather than throwing.

### 2. v1 tests (reflection-only smoke)

`tests/IviCli.Backends.Local.Tests/LocalBackendVisaInteropTests.cs`
ships three tests, each carrying both `[Requires("ni-visa")]` and
`[Trait("Category","Integration")]`:

| Test | Asserts |
| --- | --- |
| `Reflection_bindings_resolve_GlobalResourceManager_without_throwing` | The expected types are reachable on the loaded `Ivi.Visa` assembly and the factory's first-use path returns without raising. |
| `OpenAsync_against_obviously_invalid_resource_returns_BackendError` | `TCPIP0::0.0.0.0::inst0::INSTR` produces a `BackendError`, never a thrown exception. |
| `OpenAsync_against_loopback_socket_resource_invokes_reflection_path` | `TCPIP0::127.0.0.1::inst0::INSTR` (nothing listening) exercises the VISA stack's TCP path; failure is a clean `BackendError`. |

Each test uses a 500 ms device timeout so a misbehaving runtime
cannot hang CI.

### 3. CI policy

- The default unit test command (`dotnet test --filter
  "Category!=Integration"`) excludes these tests, matching the
  Husky pre-push hook (ADR 0025 §3).
- A dedicated `--filter "Category=Integration"` job runs on
  Windows runners that have the IVI Shared Components installed.
  On runners without it, all three tests skip with
  `missing prerequisite(s): ni-visa` — visible in the CI summary,
  not a silent pass.
- Tests that need a **physical instrument** are out of scope for
  this ADR. When they land they reuse the same `[Requires("ni-visa")]`
  gate plus an additional gate naming the instrument family
  (e.g. `[Requires("ni-visa","keysight-e36312a")]`); the probe
  matrix grows by one per instrument the lab can present.

### 4. Out of scope

- Cross-platform parity. `Ivi.Visa` is Windows-only; the probe
  returns false on Linux / macOS and all three tests skip.
- Vendor-specific session features (lock, trigger, SRQ) beyond what
  `ReflectionVisaSessionFactory` already implements. Those need a
  port on `IIviBackend` first.
- Multi-instrument routing tests. Each test opens at most one
  session; concurrency tests against the local backend are a
  separate follow-up.

## Consequences

- Reflection-plumbing regressions in `ReflectionVisaSessionFactory`
  surface in CI on the first run that has the IVI runtime installed,
  not at operator install time.
- The probe is a single assembly load per test process — cheap
  enough that adding it to other Local-backend tests is free if a
  future change needs the gate.
- The reflection-only stance keeps these tests deterministic: no
  instrument means no flake. The next ADR in this family will
  describe how to integrate tests that DO require an instrument,
  layered on the same `[Requires(...)]` mechanism.
