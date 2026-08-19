# Domain Glossary

Catalog of **Entities / Value Objects / Domain Services** in the IVI-CLI domain.
As the single source of truth for the ubiquitous language, code, tests, logs, and the PRD align to this vocabulary.

Classification criteria are defined in ADR 0003.

> This file is a living document. Additions and changes go through regular PRs (no ADR required, though reclassification should be justified in the PR description).

---

## Entities

Things whose identity persists independently of their attributes.

### Device

- **Identity**: `DeviceName` (alias, e.g. `psu1`)
- **Attributes**: `Resource: VisaResource`, `Timeout: Timeout`
- **Lifecycle**: created by `visa add` / removed by `visa remove` / updated by `visa add` re-registering the same name
- **Invariant**: `DeviceName` is unique within the config

### Server

- **Identity**: `ServerName` (e.g. `local`, `lab`)
- **Attributes**: `Type: ServerType`, `Host: Host?`, `Port: Port?`, `Bind: IpAddress?`
- **Lifecycle**: registered in the config / started and stopped in Phase 2
- **Note**: the `Server` definition in the config and a running "Running Server Instance" are distinct entities (the latter is introduced in Phase 2)

### Route (Phase 2)

- **Identity**: composite ID `(ServerName, PublicEndpoint)` (e.g. `(lab, hislip0)`)
- **Attributes**: `Device: DeviceName`
- **Lifecycle**: `server route add` / `server route remove`
- **Invariant**: the referenced `Device` exists in the config

### Session (singleton)

- **Identity**: fixed at one instance as "the current session"
- **Attributes**: `CurrentDevice: DeviceName?`, `CurrentServer: ServerName?`, and other volatile caches
- **Lifecycle**: loaded at process startup, mutated by `visa use` and similar commands, with no explicit termination
- **Persistence**: `state.json`
- **Note**: if multi-session support is added in the future, an identity must be introduced

---

## Value Objects

Equality is determined by the value itself; only replacement is permitted.

### Identity wrappers (strongly-typed strings)

| VO | Example | Constraints |
| --- | --- | --- |
| `DeviceName` | `psu1` | non-empty; recommended pattern is roughly `[a-z][a-z0-9_-]*` (see 0021 for naming conventions) |
| `ServerName` | `local`, `lab` | same as above |
| `HislipName` | `hislip0`, `hislip1` | per the HiSLIP convention |

### VISA / SCPI

| VO | Description |
| --- | --- |
| `VisaResource` | e.g. `TCPIP0::192.168.0.10::inst0::INSTR`. After parsing, expected to be a sum type such as `VisaResource.Tcpip` / `Usb` / `Gpib` |
| `IdnResponse` | the full `*IDN?` response |
| `IdnVendor` | the vendor portion of the IDN |
| `IdnModel` | the model portion of the IDN |
| `IdnSerial` | the serial-number portion |
| `IdnFirmware` | the firmware portion |
| `ScpiCommand` | wrapper for a SCPI string used in writes |
| `ScpiQuery` | wrapper for a SCPI string used in queries (expected to end with `?`) |

### Time and network

| VO | Description |
| --- | --- |
| `Timeout` | a semantic wrapper over `TimeSpan` (e.g. negative values disallowed) |
| `Host` | IP or hostname |
| `Port` | 1–65535 |
| `IpAddress` | for bind addresses |

### Configuration structure

| VO | Description |
| --- | --- |
| `ConfigDocument` | read model of the entire `config.toml` (holds Devices, Servers, Routes, Defaults) |
| `Defaults` | the `[defaults]` section (`Server: ServerName?`, `Device: DeviceName?`) |
| `ServerType` | enum / sum type of `Local` / `HiSlip` / `Vxi11` / `Socket` |

---

## Domain Services

Operations and invariants that do not belong to a single Entity.

### ConfigValidator

- Input: `ConfigDocument`
- Output: `Result<Validated<ConfigDocument>, ConfigError[]>`
- Responsibilities:
  - verifying that `Defaults.Device` exists in `Devices`
  - verifying that `Defaults.Server` exists in `Servers`
  - detecting duplicate names
  - other cross-entity invariants

### AliasResolver

- Input: `string` (the raw token from a CLI argument) / `ConfigDocument`
- Output: `Result<DeviceName, ResolveError>`
- Responsibility: consolidates the resolution logic for scan indices (`"1"`) and aliases (`"psu1"`) in a single place

---

## Naming conventions

- The C# class names for Entities / Value Objects / Domain Services must match the headings in this file
- Namespaces are the owning assembly plus a subcategory (e.g. `IviCli.Domain.Devices`, `IviCli.Domain.Scpi`)
- Test names follow the `<Entity>_<Behavior>_<Expectation>` pattern (detailed in 0009)

---

## Change history

This file is updated through PRs. Significant reclassifications (e.g. VO to Entity) should record their rationale in the PR description.
