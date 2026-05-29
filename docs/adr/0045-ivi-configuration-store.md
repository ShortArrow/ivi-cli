# 0045. IVI Configuration Store integration

- Status: Accepted
- Date: 2026-05-29

## Context

The IVI Foundation Configuration Store (CS) is the canonical
machine-local registry of installed IVI drivers, hardware assets,
driver sessions, and logical names. On Windows it lives at
`%PROGRAMDATA%\IVI Foundation\IVI\IviConfigurationStore.xml` and
is populated by the IVI Shared Components installer + each vendor
driver's installer.

PRD §6.5 declared `driver list` and `logical list` as part of the
`ivicli driver` / `ivicli logical` namespaces. Until Batch Y these
were verb stubs only — the CS data was never read. That made the
v0.1.0 promise of "ivi-cli helps you debug IVI/VISA environments"
ring hollow: operators investigating a flaky instrument couldn't
even confirm which drivers were installed.

This ADR commits ivi-cli to a pure-managed read-only integration
with the CS — no vendor SDK, no IVI Foundation .NET interop, no
COM calls. We parse `IviConfigurationStore.xml` directly.

## Decision

### 1. Scope: read-only

v1 only enumerates. Mutation (creating logical names, registering
drivers) stays out of scope — those operations are managed by
vendor installers + IVI Configuration Server GUI, and reproducing
them in CLI form would mean reverse-engineering write semantics
not documented in any public spec.

### 2. Surface

| Verb | Purpose |
| --- | --- |
| `ivicli driver list` | Enumerate every `<SoftwareModule>` entry: name, description, module path, prefix. |
| `ivicli driver list --json` | Same, machine-readable. |
| `ivicli logical list` | Enumerate every `<LogicalName>` entry: name, description, bound driver-session name. |
| `ivicli logical list --json` | Same, machine-readable. |

Out of scope for v1:

- `driver show <name>` — detail view (deferred until use case
  surfaces).
- `logical show <name>` — same.
- Driver session / hardware asset enumeration — derivable from
  the store but not yet asked for.

### 3. Path resolution

The store lives at an OS-conventional location:

| OS | Path |
| --- | --- |
| Windows | `%PROGRAMDATA%\IVI Foundation\IVI\IviConfigurationStore.xml` |
| Linux / macOS | `/etc/ivi-foundation/IviConfigurationStore.xml` (a deterministic non-existent default — IVI Shared Components are Windows-only, so the not-found path is the common case) |

Resolution lives in
`InfrastructureServiceCollectionExtensions.AddIviCliIviConfigurationStore`.
Override via the optional `storePath` parameter — used by tests
and (future) by an env var if operator demand surfaces.

### 4. Parser

`XmlIviConfigurationStore` (Infrastructure) parses the file with
`System.Xml.Linq.XDocument`:

- Matches by `XElement.Name.LocalName` so XML namespace prefix
  changes across IVI versions don't break the parser.
- Permissive: missing optional sub-elements yield `null` rather
  than parse errors. The store's exact schema differs between
  IVI Shared Components 2.x and 3.x; the v1 reader tolerates
  both.
- An entry without a `<Name>` is silently dropped (corrupt
  fragment).

### 5. Cross-platform behaviour

The IVI ecosystem is Windows-centric. On non-Windows hosts the
store file does not exist; the CLI reports a friendly
`(no IVI Configuration Store at <path>)` and exits 0 — this is
informational, not an error. ADR 0014's exit-code policy treats
"feature unavailable on this platform" as a soft outcome.

### 6. Error model

`IviConfigurationStoreError` sum:

- `IviConfigurationStoreNotFound(Path)` — file missing
  (informational severity; exit 0 with a helpful note).
- `IviConfigurationStoreReadFailure(Path, Inner)` — IO failure
  (warning severity).
- `IviConfigurationStoreParseFailure(Detail, Inner)` — XML
  malformed (warning severity).

## Consequences

- **Debugging story closes a gap**: operators can ask
  `ivicli driver list` before opening any session, get a complete
  inventory, and confirm whether the IVI runtime is what they
  expect.
- **No new runtime dependencies**: pure managed XDocument; no
  Ivi.* NuGet packages, no COM interop. Cross-platform clean.
- **Read-only is honest**: mutation would require either calling
  into COM (Windows-only, ties us to one platform forever) or
  re-implementing the IVI write semantics (out of scope, risky
  to get right without breaking the IVI Configuration Server's
  notion of consistency).
- **Non-Windows graceful**: Linux / macOS operators see a single
  informational line, not a stack trace or hard error.

## Out of scope (v1)

- **Mutation** of the store — see §1.
- **Driver session / hardware asset enumeration** — the data is
  in the same XML, but no operator has asked for it. Adding the
  verbs is mechanical when needed.
- **Detail views** (`driver show`, `logical show`) — see §2.
- **Override env var** for the store path. The constructor takes
  a string; the env-var wiring lands when an operator asks.
- **IVI version awareness** — we don't surface which IVI Shared
  Components version produced the store. Could be added via the
  store's `<Date>` / `<RevisionString>` elements when present.

## Related work

- IVI Foundation, "IVI Configuration Server", https://www.ivifoundation.org/
  (specification documents).
- ADR 0029 (VXI-11 gateway) — separate path; that ADR is about
  the wire protocol, this ADR is about driver metadata
  introspection.
- ADR 0010 (DI) — `IIviConfigurationStore` follows the
  established port / Infrastructure adapter pattern.
- ADR 0024 (documentation policy) — `docs/PRD.md` §14 MVP now
  lists `driver list` + `logical list`; the Japanese mirror is
  updated lockstep.

## Verification

- `dotnet test --filter "Category!=Integration"` covers
  `XmlIviConfigurationStoreTests` (6 tests):
  - SoftwareModule + LogicalName happy paths.
  - Missing file → `IviConfigurationStoreNotFound`.
  - Malformed XML → `IviConfigurationStoreParseFailure`.
  - Namespaced XML (real IVI 3.x shape) parses via local-name match.
  - Entry without `<Name>` is silently dropped.
- Manual on Windows with IVI Shared Components installed:
  `ivicli driver list` lists every vendor driver in the store.
- Manual on bare Linux (no IVI ecosystem):
  `ivicli driver list` prints
  `(no IVI Configuration Store at /etc/ivi-foundation/IviConfigurationStore.xml)`
  and exits 0.
