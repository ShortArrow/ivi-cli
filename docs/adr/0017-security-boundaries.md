# 0017. Security Boundaries

- Status: Accepted
- Date: 2026-05-21

## Context

PRD positions Phase 1 as a strictly local CLI tool (no network listener, no auth), and Phase 2 as a remote instrument gateway that exposes an HiSLIP-compatible server plus a management API. The security surface differs sharply between phases:

- **Phase 1** is concerned with input validation at trust boundaries, log hygiene, local file permissions, and dependency supply-chain hygiene. No network listener, no credentials, no audit trail required.
- **Phase 2** introduces authentication, transport security, audit logging, and secret storage.

This ADR fixes the Phase 1 policy and records Phase 2 intent as placeholders to be expanded when Phase 2 work begins.

## Decision

### 1. Threat model scope (Phase 1)

**In scope:**

- Input validation at trust boundaries (CLI arguments, `config.toml` values, `session.json` reload)
- Sensitive-data leakage via logs (VISA resource hostnames, instrument serial numbers, etc.)
- Local file permissions for state files
- Dependency supply-chain hygiene (NuGet feed restriction, version locking, automated updates)
- Error message hygiene (no internal paths or raw config values exposed to stdout/stderr)

**Out of scope (Phase 1):**

- Authentication and authorization (no management API yet)
- Transport security / TLS (no network listener yet)
- Audit logging (no auditable events yet)
- Rate limiting and DoS protection

The out-of-scope items are addressed by §6 as Phase 2 placeholders.

### 2. Input validation: Value Object constructors return Result

All untrusted input enters the domain through Value Object constructors that return `Result<T, TError>` (per ADR 0023).

Required validations:

| VO | Validation |
| --- | --- |
| `DeviceName.From(string)` | non-empty; recommended pattern `[a-z][a-z0-9_]*`; length cap |
| `ServerName.From(string)` | same as `DeviceName` |
| `HislipName.From(string)` | conforms to HiSLIP naming convention |
| `VisaResource.Parse(string)` | known transport prefix, structurally valid components |
| `ScpiCommand.From(string)` | no embedded control characters except documented terminators; length cap |
| `ScpiQuery.From(string)` | as `ScpiCommand`, plus a trailing `?` |
| `Host.From(string)` | valid hostname or IPv4/IPv6 literal |
| `Port.From(int)` | 1–65535 |
| `Timeout.From(TimeSpan)` | non-negative, reasonable upper bound |

No untyped `string` for these concepts may flow through the domain layer (per ADR 0003 Anti-Corruption Layer). FluentValidation and DataAnnotations are not adopted; validation lives in the VO itself.

### 3. Log hygiene: `ToLogString()` masking

Each Value Object that may carry sensitive content exposes a `ToLogString()` method that returns a masked form. Logging code uses `ToLogString()` (not `ToString()`) for `{Device}`-style placeholders.

Examples:

- `VisaResource.Tcpip("192.168.0.10", "inst0").ToLogString()` → `TCPIP0::***::INSTR`
- `IdnResponse.ToLogString()` → masks the serial-number portion
- `VisaResource.Usb(...).ToLogString()` → masks the serial portion

Rules:

- The CLI must not log unmasked `VisaResource` at `Information` level. `Debug` may include unmasked forms for diagnostics.
- The `--json` output of `visa status` and similar commands emits unmasked resource strings (the user is the operator and the output is local). Network-bound responses (Phase 2) re-evaluate this.
- `ScpiCommand` / `ScpiQuery` are logged in full at `Debug`; `Information` logs only command identity (e.g. command name, not full payload) when the payload may include measurement data.

A future Roslyn analyzer rule may enforce that `ILogger` placeholder arguments of VO types call `ToLogString()`; this is not implemented in Phase 1.

### 4. Local file permissions

- **`session.json`** (state directory): **user-only** permissions are set explicitly on every write.
  - Unix-like: `chmod 0600` after atomic write-and-rename.
  - Windows: NTFS ACL granting only the current user identity (read/write).
- **`config.toml`**: default umask / inherited ACL. The file is intended to be human-editable (`vim`, `nano`, GUI editors) and contains no secrets in Phase 1. Restricting it would create friction without benefit.
- The atomic write pattern (temp file → set permissions → rename) is used for both files to avoid partial writes; permission setting is part of the rename-into-place step.
- Editor workflow is unaffected: the owner of the file can read and write `session.json` normally; swap and backup files inherit the directory's umask, not the source file's stricter mode.

### 5. Dependency supply chain

- **Lock files**: `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` is set in `build/Directory.Build.props`. `packages.lock.json` is committed for every project.
- **Locked restore in CI**: `dotnet restore --locked-mode` is enforced by CI (ADR 0020).
- **NuGet feed**: nuget.org only. Adding any private or third-party feed requires an ADR update.
- **Automated updates**: Dependabot is configured (`.github/dependabot.yml`) to open weekly grouped PRs for NuGet, GitHub Actions, and `dotnet-tools.json`. The exact configuration is added when the repo gains the corresponding files.
- **Supply-chain attack surface review** is implicit in PR review of Dependabot updates; high-risk upgrades (e.g. a transitive dependency added) warrant explicit attention.

### 6. Phase 2 placeholders (security posture once the gateway is exposed)

When the management API and HiSLIP-compatible gateway are introduced, the following baseline applies. Each item will be elaborated when the corresponding feature is implemented:

| Concern | Phase 2 baseline | Shipped |
| --- | --- | --- |
| Management API authentication | mTLS preferred; PAT token-based authentication as a lab-convenience fallback | PAT (ADR 0036), mTLS (ADR 0039), PAT scopes + expiry (ADR 0044) |
| Management API transport | TLS opt-in via `[api.tls]`; plaintext HTTP remains the historical default for trusted LANs (ADR 0039) | ADR 0039 |
| HiSLIP transport | Follows the HiSLIP specification (plain by default); operators are expected to deploy on a trusted LAN. Plain-text mode is documented as a deployment constraint | ADR 0007 §1 |
| Secret storage | OS-native keychain (Windows Credential Manager / macOS Keychain / Linux Secret Service via libsecret). No plaintext credentials in `config.toml` |
| Audit log | Append-only NDJSON via [ADR 0043](0043-audit-log.md). v1 emits auth + API-request events; config-mutation + gateway-lifecycle emissions accrete in follow-ups |
| Rate limiting | Per-source connection caps; details deferred |
| Privilege model | The CLI process runs as the invoking user; no setuid / setcap requirements |
| Plugin loading | [`[plugins] enabled = false`](0013-plugin-system.md) default-off; opt-in opens an in-process full-trust path for vendor DLLs. Allowlist gating; signature validation deferred to v2 |

### 7. Miscellaneous Phase 1 rules

- **CLI argument handling**: System.CommandLine parses arguments. The CLI never composes shell strings; `Process.Start` (if used) takes argv arrays, not command lines.
- **Error messages**: stderr and exit-code messages do not embed absolute file paths, raw config values, or stack traces. Detailed diagnostics go to log files (file destination is per the logging ADR 0011).
- **`--json` output**: serialized fields are an explicit whitelist enforced at the type level (records with `[JsonInclude]` or `JsonSerializerOptions` configured per type). Fields like passwords or tokens are physically absent in Phase 1; the policy ensures they remain absent in Phase 2 by default.
- **Test data**: realistic-looking but fictitious IDN responses, hostnames, and ports in test fixtures. No real device identifiers, internal IP ranges, or vendor-private model codes committed.

## Consequences

**Pros**

- Trust boundaries are explicit and small in Phase 1; nothing in the design accidentally exposes a network surface or stores credentials.
- VO-based validation makes invalid state unrepresentable at the domain level; security follows from type safety rather than runtime checks scattered across handlers.
- Log masking via `ToLogString()` keeps observability viable without leaking instrument internals.
- Dependency locking + Dependabot strikes a balance between supply-chain hygiene and maintenance burden.

**Cons**

- Phase 1 protections do not address most "interesting" security threats, because there is no network surface. A reader expecting auth/TLS coverage will need to wait for Phase 2.
- Mandatory user-only file permissions on `session.json` add platform-specific code (Unix `chmod` vs Windows ACL).
- Locked NuGet restore can fail noisily on transient feed issues; this is intentional.

**Mitigations**

- The Phase 2 placeholders (§6) make the intended security posture for the gateway visible early, so design decisions in the meantime do not preclude them.
- The permission-setting code is centralized in `IviCli.Infrastructure` (one helper for state files), keeping the platform branching contained.
- `--locked-mode` failures are easy to diagnose; CI emits an actionable error.
