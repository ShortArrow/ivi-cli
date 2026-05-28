# 0034. Management API (HTTP JSON)

- Status: Accepted
- Date: 2026-05-27

## Context

PRD §7.5 declares a Management API as a control plane sibling to the
VISA transport gateways (HiSLIP / VXI-11 / Socket). Its consumers are
AI agents (PRD §15), custom dashboards, CI scripts, and integration
tests that need device list / status / scenario list / remote SCPI
without speaking VISA. The PRD left "gRPC or HTTP JSON" open.

This batch picks **HTTP JSON over ASP.NET Core minimal API** as the v1
transport and ships read-only resource endpoints plus SCPI query /
write. gRPC is deferred to a separate ADR if a real consumer demands
it; HTTP JSON's reach (curl, jq, Python `requests`, browser `fetch`,
every AI agent tool framework) is enough for the v1 audience.

## Decision

### 1. Technology

- ASP.NET Core 10 minimal-API surface inside a new `IviCli.Api`
  assembly (`Sdk="Microsoft.NET.Sdk.Web"`, library output type).
- Embedded in the same OS process as the CLI: `ivicli api start`
  builds a `WebApplication` from the CLI's already-built
  `IServiceProvider` (forwarding the Application handlers as
  singletons), so no separate composition root, no inter-process
  protocol, no PID-coordination fan-out.
- `Microsoft.AspNetCore.OpenApi` ships an `/openapi/v1.json` document
  for tooling (`swagger-codegen`, AI-agent integrations).

### 2. v1 scope

| Method | Path | Body | Response | Handler |
| --- | --- | --- | --- | --- |
| `GET` | `/healthz` | — | `{ "status": "ok" }` | constant |
| `GET` | `/openapi/v1.json` | — | OpenAPI 3.x document | `MapOpenApi()` |
| `GET` | `/v1/devices` | — | `DeviceListingDto` | `ListDevicesQueryHandler` |
| `GET` | `/v1/devices/{name}/status` | — | `DeviceStatusDto` | `StatusDeviceCommandHandler` |
| `GET` | `/v1/servers` | — | `ServerListingDto` | `ListServersQueryHandler` |
| `GET` | `/v1/scenarios` | — | `ScenarioListingDto` | `ListScenariosQueryHandler` |
| `POST` | `/v1/devices/{name}/query` | `ScpiRequestDto` | `ScpiQueryResponseDto` | `QueryDeviceCommandHandler` |
| `POST` | `/v1/devices/{name}/write` | `ScpiRequestDto` | `ScpiAckDto` | `WriteDeviceCommandHandler` |

JSON contracts (DTO records under `IviCli.Api.Contracts`) are
intentionally **distinct from CLI stdout shapes** — the OpenAPI
document is the API surface and stays stable independently of CLI
output formatting.

When the operator leaves `[pool] enabled = true` (the default,
ADR 0038), repeated `POST /devices/{name}/query` requests against
the same device share a single underlying wire session and pay no
re-open cost — relevant for AI-agent loops and dashboards that
poll an instrument at high frequency.

### 3. Error envelope

Every non-2xx response carries `{ "error": { "code": "<stable>", "message": "<text>" } }`.

| Outcome | Status | Code |
| --- | --- | --- |
| Missing / empty `scpi` field | 400 | `missing_scpi` |
| Invalid SCPI (`ScpiError`) | 400 | `invalid_scpi` |
| Unknown alias / `NoTarget` | 404 | `device_not_found` |
| Transport / backend failure | 502 | `backend_failure` |
| Config / session store failure | 503 | `config_store_failure` |
| Scenario store failure | 503 | `scenario_store_failure` |
| Fall-through | 500 | `internal_error` |

### 4. Lifecycle

CLI verbs (`src/IviCli.Cli/Commands/ApiCommand.cs`):

- `ivicli api start [--port 8080] [--bind 127.0.0.1]` — foreground
  listener. Writes a PID file via the existing `IServerProcessRegistry`
  under the reserved `ServerName` **`ivi-management-api`** so a
  sibling `api stop` can locate the running process. Cleans up the
  PID on exit. Cancellation token (Ctrl+C) propagates through the
  `IHost.RunAsync(ct)` cast.
- `ivicli api stop` — reads the reserved PID, sends a graceful exit
  (CloseMainWindow on Windows, Kill non-tree on POSIX) with a 5 s
  grace window, then force-kills. Stale PID files clean up.

Default bind = `127.0.0.1`. Non-loopback bind logs a Warning that
authentication is not implemented in v1 (see §5).

### 5. Security stance

- **Token-based authentication landed in [ADR 0036](0036-management-api-authentication.md).**
  `Authorization: Bearer <token>` gates HTTP routes;
  `Sec-WebSocket-Protocol: ivi-cli-pat.<token>` gates the WS handshake.
  Loopback bind without tokens is still permitted (the local-only
  stance); non-loopback bind requires ≥ 1 configured token or an
  explicit `--allow-anonymous` opt-out.
- **Transport-layer security landed in [ADR 0039](0039-management-api-tls.md).**
  `[api.tls]` (config) or `--tls*` (CLI) enable HTTPS / mTLS. TLS is
  opt-in; the historical plaintext default still applies for trusted
  LANs. PAT and TLS compose orthogonally.
- The request body cap is the ASP.NET Core / Kestrel default (30 MiB);
  no per-route limit in v1.
- CORS is not configured (no `AddCors`) — browsers honour same-origin.

### 6. Layer placement

- `IviCli.Api` depends only on `IviCli.Application` (and transitively
  `IviCli.Domain`).
- `IviCli.Cli` ProjectReferences `IviCli.Api` so the lifecycle verbs
  can call `IviCliApiBuilder.Build`. No backwards reference.
- Architecture tests (`tests/IviCli.Cli.Tests/Architecture/DependencyDirectionTests.cs`)
  add `IviCli.Api` to the upstream-forbidden lists for Domain /
  Application / Infrastructure to formalise the boundary.

## Out of scope (v2 candidates)

- **Authentication** (API tokens, then mTLS) — lands before any
  intended non-loopback deployment.
- **gRPC transport** — a sibling ADR if a consumer needs it; the
  Application layer is already protocol-agnostic.
- **Server lifecycle endpoints** (`POST /v1/servers/{name}/start`) —
  needs authn first.
- **Scenario import endpoint** (`POST /v1/scenarios` from NDJSON) —
  Batch H's `mock scenario import` is the CLI face; the API equivalent
  is a v2 add atop the same handler.
- **WebSocket streaming** (`/v1/devices/{name}/watch`) — covered by
  the separate PRD §15 "VISA-over-WebSocket" planned item.
- **Audit log endpoint** — Batch F's `IVICLI_CAPTURE` already provides
  the file-based audit; exposing the file (and a tail subscription)
  over the API is a v2 concern.

## Consequences

- AI agents and dashboards have a stable HTTP JSON control plane —
  one process, one port, OpenAPI document for tooling.
- The v1 scope is intentionally small: no new endpoints beyond what
  existing CLI handlers already cover.
- Future expansion (authn, more endpoints, gRPC, WebSocket) adds
  surface area without restructuring; the layer boundaries set here
  are already correct.
- Two new package references: `Microsoft.AspNetCore.OpenApi`,
  `Microsoft.AspNetCore.TestHost` (test-side only).
