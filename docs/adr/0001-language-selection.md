# 0001. Language Selection

- Status: Accepted
- Date: 2026-05-21

## Context

The implementation language affects every downstream technical choice: standard library, NuGet ecosystem access (NI-VISA bindings, HiSLIP libraries, etc.), CLI framework, build / packaging, and contributor familiarity.

The PRD (§12) already declares the target stack — C# with `System.CommandLine`, `Tomlyn`, MEL, etc. This ADR records the language and runtime selection, and pins the runtime / language version explicitly so that downstream ADRs (e.g. 0014, 0023) can rely on specific language features (records, primary constructors, sealed-type pattern matching, source generators, file-scoped namespaces, etc.).

## Decision

### 1. Language: C#

Adopt **C#** as the sole implementation language.

Rationale:

- First-class bindings to NI-VISA and IVI Foundation libraries are distributed as managed .NET assemblies, making C# the lowest-friction host language for the data plane.
- The .NET ecosystem has mature, well-supported choices for every PRD §12 dependency: System.CommandLine, Tomlyn, Serilog, NSubstitute, Shouldly, etc.
- F# is rejected here despite its FP affinity: vendor SDK ergonomics (NI-VISA, HiSLIP wrappers) lean C#-first, and the team's expressed FP-leaning style (ADRs 0023, 0010) is achievable in modern C# (records, pattern matching, sealed sum types, `Result<T, TError>`).

### 2. Runtime: latest LTS .NET

Target the **latest LTS release of .NET** for production builds. As of this ADR's acceptance, that is **.NET 10** (released November 2025, supported through November 2028).

- `<TargetFramework>net10.0</TargetFramework>` in every project's csproj (centralized via `build/Directory.Build.props` per ADR 0021).
- LTS rather than STS to align with the project's lifetime and the conservatism expected by automation / lab users.
- When the next LTS ships (.NET 12, expected November 2027), the upgrade is performed in a single PR after a transition window. The change is editorial to this ADR.

### 3. C# language version: latest stable that ships with the LTS SDK

Set `<LangVersion>latest</LangVersion>` in `build/Directory.Build.props`. With .NET 10 SDK this resolves to **C# 14**, which is the assumed feature baseline for code authored under this ADR.

Features explicitly relied upon by other ADRs:

- `record` and positional records (immutable domain types, per ADR 0023).
- Sealed type hierarchies + `switch` expressions (sum-type pattern, per ADR 0023).
- Primary constructors (concise constructor injection, per ADR 0010).
- Nullable Reference Types enabled globally (per ADR 0023).
- File-scoped namespaces (style consistency, enforced by CSharpier per ADR 0025).
- `LoggerMessage` source generation (hot-path logging, per ADR 0011).

### 4. SDK version

Use the latest SDK that targets `net10.0`. Concrete version pinning is delegated to **`global.json`** at the repository root, written when the C# scaffolding is generated. The file constrains contributors to the same SDK feature band (`rollForward: latestFeature`).

### 5. Other languages: out of scope

No other language is adopted. Helper scripts may be written in PowerShell (for Windows-specific workflows) or POSIX shell (for hooks already invoked by Husky.Net per ADR 0025), but **no production code** outside C# is permitted in this repository.

## Consequences

**Pros**

- A single-language codebase keeps the contributor toolchain minimal: install .NET SDK and you are done (plus `dotnet tool restore` for project-local tools).
- LTS selection aligns with vendor SDK availability and lab-environment conservatism.
- C# 14 features are sufficient to express the FP-leaning style declared in 0023 without third-party language extensions.
- Upgrading to a newer LTS is a localized change (`Directory.Build.props` + `global.json`); the rest of the codebase only depends on language features, not runtime peculiarities.

**Cons**

- Tying to the latest LTS means contributors must install .NET 10 (or newer) — a barrier for users running older Linux distributions whose package managers lag .NET releases. Mitigated by Microsoft's official install scripts.
- `<LangVersion>latest</LangVersion>` follows SDK updates rather than pinning a fixed value, so a developer with an older SDK installed locally may see different feature availability. The `global.json` floor protects against the underestimate direction.

**Mitigations**

- `global.json` pins the SDK floor; CI uses the same floor.
- Documentation in CONTRIBUTING (when written) calls out the required SDK version explicitly.
- When the next LTS arrives, the upgrade ADR-edit + `Directory.Build.props` + `global.json` changes are performed in one PR with a short transition note in CHANGELOG.
