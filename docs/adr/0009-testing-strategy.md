# 0009. Testing Strategy

- Status: Accepted

## Context

Development of this project requires the TDD cycle (Red-Green-Refactor), and tests are written in Given/When/Then form, focusing on state differences.
PRD §13 already declares the test stack (xUnit / NSubstitute / Shouldly / Logging.Abstractions / System.IO.Abstractions.TestingHelpers) and the Phase 1 test targets.
This ADR fixes **how far we extend testing and where we draw the line**.

Dependent decisions already in place:

- 0021 decided that test projects under `tests/` mirror `src/` 1:1, and that integration tests are separated by trait.
- 0003 decided that the Application layer exposes ports and Backends are adapters (making the mock surface explicit).
- 0003 decided that the FakeBackend is shipped as a standalone assembly `IviCli.Backends.Fake`.

## Decision

### 1. Test categories and gating

| Category | Scope | xUnit Trait | CI gating |
| --- | --- | --- | --- |
| **Unit** | Pure logic in Domain / Application / FakeBackend / Cli | (none, default) | Required on PR |
| **Integration** | Real VISA / real files / real sockets / real processes | `Category=Integration` | Nightly + manual trigger |
| **Architecture** | Dependency-direction violations from 0021 (NetArchTest) | `Category=Architecture` | Required on PR |

- Default PR run: `dotnet test --filter "Category!=Integration"`
- Integration runs on the nightly workflow (details in 0020).
- Architecture tests run by default like Unit (the Trait exists only for classification).

### 2. TDD cycle

- Even in areas without existing tests, write Red first.
- When Red is impractical due to environment constraints (real hardware requirements, etc.), add a minimal **characterization test** up front.
- Instead of "checking correctness by looking at logs," pin behavior down with tests.

### 3. Test naming

Use `<MethodOrBehavior>_<Scenario>_<Expectation>` as the base form. Align vocabulary with the domain glossary.

```csharp
[Fact] public void AddDevice_WithDuplicateName_ReturnsConflictError();
[Fact] public void ConfigValidator_WhenDefaultDeviceMissing_ReturnsValidationError();
[Fact] public async Task QueryAsync_OnDisconnectMidQuery_ReturnsTransportError();
```

Class names follow `<TypeUnderTest>Tests` (e.g. `AddDeviceCommandHandlerTests`).

### 4. AAA / Given-When-Then

Write the body of each test in three Given/When/Then sections, focusing on the "expected state difference."

```csharp
[Fact]
public void AddDevice_WithDuplicateName_ReturnsConflictError()
{
    // Given
    var config = ConfigBuilder.Empty.WithDevice("psu1", "TCPIP0::host::inst0::INSTR");
    var handler = new AddDeviceCommandHandler(config.AsStore());

    // When
    var result = handler.Handle(new AddDeviceCommand("psu1", "USB0::0x0699::..."));

    // Then
    result.ShouldBeError(ConflictError.DuplicateDeviceName);
}
```

### 5. Mocking policy

- **Mock only Ports**: `IIviBackend`, `IConfigStore`, `ISessionStore`, `IClock`, `IFileSystem`.
- **Do not mock**: Domain Entity / Value Object / Domain Service (use the real ones).
- **Prefer `IviCli.Backends.Fake` for Backend-related tests.** Use NSubstitute mocks of `IIviBackend` only in Application-layer / Cli-layer tests.
- Rationale: the Fake is a "near-real fake" that honors domain invariants, while a mock encodes only a single-shot contract. Do not re-implement behavior tests in mocks when the Fake can express them.

### 6. FakeBackend fault injection

`IviCli.Backends.Fake` is not a mere echo — it provides a builder API for tests.
The exact API will be settled during implementation, but the intended feel is:

```csharp
var fake = new FakeBackend();
fake.ConfigureDevice("psu1", idn: "FAKE,PSU,0,1.0");
fake.OnOpen("psu1").FailWith(VisaError.ResourceNotFound);
fake.OnQuery("psu1", "*IDN?").RespondWith("FAKE,PSU,0,1.0").After(10.ms);
fake.OnQuery("psu1", "MEAS:VOLT?").Timeout();
fake.SimulateDisconnect("psu1", after: 100.ms);
```

All lifecycle test targets in PRD §13.3 (open success/failure, query/read timeout, disconnect mid-query, reconnect, dispose-once, online/offline determination) must be expressible with the Fake.

### 7. Additional tooling

| Tool | Purpose | Adopted |
| --- | --- | --- |
| **NetArchTest** | Detect dependency-direction violations, layer violations, missing interface implementations, etc. from 0021 | Adopted |
| **Verify** (snapshot) | `--json` output contract, help text, rendered output | Adopted |
| **FsCheck** (property-based) | VO invariants (VisaResource parse/serialize roundtrip, Timeout range constraints, etc.) | Adopted with scope limited to VO-related areas; no test uses it yet, so `FsCheck.Xunit.v3` is added with the first one |
| **coverlet** (`coverlet.mtp`) | Coverage measurement | Adopted (visualization only, no numeric gate) |
| **Stryker.NET** (mutation) | Verifying test quality | **Not adopted** (overkill for Phase 1) |

### 8. Shared Test Helper: `tests/IviCli.TestKit/`

Create a shared library that consolidates:

- **Test Data Builders**: `ConfigBuilder`, `SessionStateBuilder`, `DeviceBuilder`, etc.
- **FakeBackend Schedule DSL**: the builder API from §6.
- **Custom Shouldly extensions**: Result-type assertions such as `result.ShouldBeError(...)`.
- **Verify configuration**: snapshot placement conventions and normalization rules.
- **Trait constants**: `Categories.Integration`, `Categories.Architecture`.

`tests/IviCli.TestKit/` lives under `tests/` rather than `src/` (it is outside the 1:1 mirror rule from 0021 and treated as test infrastructure).

### 9. Coverage policy

- Measure with coverlet and show the result in the CI job's summary, merged across test projects by ReportGenerator — visualization only. No external coverage service is used.
- **No numeric gate.** The project's policy is "stick to TDD as a behavior," and tests written after the fact to meet a coverage number distort that goal.
- A script that mechanically detects coverage drops is acceptable for informational purposes (non-blocking).

### 10. async tests

- Backend-related tests return `async Task` by default.
- Set timeouts **explicitly on the test side as well**: standardize on `[Fact(Timeout=5000)]` to prevent deadlocks and infinite loops from hanging CI.
- `CancellationToken` is already propagated to every method of the Backend port (0003 / 0015); tests must exercise the cancellation path explicitly.
- A test that has no token of its own to pass passes `TestContext.Current.CancellationToken`, which xUnit cancels when the run is cancelled or the test times out. The xUnit1051 analyzer enforces this only in the bodies of methods marked `[Fact]` or `[Theory]`; a `[Requires]` test, a helper or a lambda passes the token by hand. Cleanup that must run even after cancellation, such as closing a session in `finally`, passes `CancellationToken.None`.

### 11. Test framework and runner

- Tests use **xUnit v3**. v2 receives only maintenance; v3 adds `TestContext.Current.CancellationToken` (§10), skipping at run time (`Assert.Skip`, `SkipUnless`, `SkipWhen`), and custom traits through `ITraitAttribute.GetTraits()` without a discoverer.
- The runner is **Microsoft.Testing.Platform**, not VSTest. Every test project is an executable (`OutputType` `Exe`, set in `tests/Directory.Build.props` with `UseMicrosoftTestingPlatformRunner`), and `global.json` makes `dotnet test` drive it. A run in which a test project executed no test fails with exit code 8. The integration run ignores that code, because most projects have no integration test, and so it also passes when its filter matches nothing; the unit run does not, because every project has unit tests.
- `dotnet test --filter` keeps the VSTest filter syntax: xUnit's platform integration accepts it, so `--filter "Category!=Integration"` works as before.
- Extensions are packages: `coverlet.mtp` for coverage (`--coverlet --coverlet-output-format cobertura`) and `Microsoft.Testing.Extensions.HangDump` (`--hangdump`). TRX comes from xUnit (`--report-xunit-trx`), one file per test project.
- `coverlet.mtp` leaves out compiler-generated code, so a positional record such as `Device` has no lines to cover, and its default exclusions, built from the test project's dependencies, include the CLI assembly `ivicli`. CI therefore names what to measure: `--coverlet-include "[IviCli.*]*" --coverlet-include "[ivicli]*" --coverlet-exclude "[IviCli.TestKit]*" --coverlet-exclude "[IviCli.Plugin.Sample]*"`. An explicit exclusion replaces the default list.
- CI writes a heap dump when no test has started or finished for five minutes, which ends the run with exit code 7, and uploads the dump when the job fails.
- An IDE's test explorer must support Microsoft.Testing.Platform; the VSTest adapter `xunit.runner.visualstudio` is no longer referenced.
- `IviCli.TestKit` references `xunit.v3.extensibility.core`, not `xunit.v3`: the latter turns a project into a test executable.

## Consequences

**Pros**

- A single mock library plus a single fake gives a consistent feel when writing tests.
- Architecture tests catch CA violations early (reducing reliance on manual review).
- Snapshots pin the `--json` output contract in CI (the basis for AI/CI integration in PRD §9).
- The absence of a coverage gate suppresses "tests written to hit a number."

**Cons**

- Up-front investment to implement the FakeBackend fault injection DSL.
- More NuGet dependencies from NetArchTest / Verify / FsCheck / coverlet (test-side only; no impact on the shipped binary).
- Sharing via TestKit creates coupling across tests; careless extension can raise coupling further.

**Mitigations**

- Start the FakeBackend DSL from the minimum and limit it to satisfying the lifecycle cases in PRD §13.3.
- Restrict what goes into TestKit to **common helpers referenced from two or more tests** (strict YAGNI).
- Significant changes to test infrastructure can record their rationale in the PR description without needing a new ADR.
