# 0035. VISA-over-WebSocket

- Status: Accepted
- Date: 2026-05-27

## Context

PRD §15 listed "VISA-over-WebSocket" as a Planned feature. Browsers
and Node clients cannot speak HiSLIP / VXI-11 / raw Socket directly;
they speak HTTP and WebSocket. Without this bridge, a browser-based
dashboard or a JavaScript AI-agent runtime cannot drive a registered
VISA device through ivi-cli.

Batch I shipped the HTTP Management API ([ADR 0034](0034-management-api.md));
this ADR extends the **same listener process** with a WebSocket
subprotocol so one `ivicli api start` exposes both REST and a live
SCPI duplex stream on one port.

## Decision

### 1. Co-located with the Management API

The WebSocket route is mounted inside `IviCli.Api`, not as a new
`ServerType.WebSocket` gateway. Two reasons:

- Browsers expect HTTP and WS on the **same origin / port**. A
  separate gateway port would force CORS gymnastics or a reverse
  proxy.
- The Management API process already wires `QueryDeviceCommandHandler`
  / `WriteDeviceCommandHandler` / `IConfigStore` into DI. Reusing
  them is one minimal-API hook.

The existing VISA gateway taxonomy (HiSLIP / VXI-11 / Socket) targets
VISA clients that speak those wire protocols. Browsers are a
different audience; conflating them under `server start` would muddy
the UX.

### 2. Wire format

- Handshake URL: `ws://<host>:<port>/v1/devices/{name}/visa`.
- One JSON object per WebSocket frame, **text** type, **never
  fragmented**, UTF-8.
- `{name}` is the registered device alias; the connection is bound to
  one device for its lifetime. Switching devices = new connection.
- 64 KiB max frame size on the server.

Client → Server:

```json
{"op":"query","scpi":"*IDN?"}
{"op":"write","scpi":"OUTP ON"}
```

Server → Client (one event per inbound frame plus protocol errors):

```json
{"event":"response","scpi":"*IDN?","response":"ACME,…","latencyMs":12}
{"event":"ack","scpi":"OUTP ON"}
{"event":"error","code":"<stable>","message":"<text>"}
```

### 3. Error codes

Locked in step with [ADR 0034 §3](0034-management-api.md):

| Code | When |
| --- | --- |
| `protocol_error` | Malformed / unknown / binary / fragmented frame |
| `missing_scpi` | `scpi` field absent or whitespace |
| `invalid_scpi` | Application-layer SCPI validation rejected |
| `device_not_found` | Path alias not registered |
| `backend_failure` | Transport / backend IO error |
| `config_store_failure` | Config / session store unreadable |
| `internal_error` | Unhandled exception (also sent before 1011 close) |

### 4. Close codes

| Code | When |
| --- | --- |
| 1000 NormalClosure | Client closed; or server cleanly tore down (incl. unknown-device path after sending an `error` event first) |
| 1011 InternalServerError | Unhandled server exception |

Custom private-use codes (4xxx) were considered for `device_not_found`
but discarded — TestHost surfaces them inconsistently across .NET
versions, and the structured `error` event already carries the same
information in a stable shape.

### 5. Security stance

- **Token authentication landed in [ADR 0036](0036-management-api-authentication.md).**
  Browser clients pass the token via the
  `Sec-WebSocket-Protocol: ivi-cli-pat.<token>` header on the
  upgrade handshake (browsers can't set custom headers on WS).
  Server-side validation goes through the same middleware as the
  HTTP routes — one envelope, one rule set.

### 6. Layer placement

- DTOs + codec live in `IviCli.Api/WebSockets/` — no ASP.NET Core
  dep on `VisaWebSocketCodec`, only on `VisaWebSocketEndpoint`.
- The handler reuses `QueryDeviceCommandHandler` /
  `WriteDeviceCommandHandler` from `IviCli.Application/Devices/`. No
  new Application surface.
- `IviCli.Api` continues to depend only on `IviCli.Application` (and
  transitively `IviCli.Domain`); architecture tests already enforce
  this.

## Out of scope (v2 candidates)

- **Binary frames** for IEEE 488.1 raw byte payloads (waveform
  captures, screen dumps). v1 is text JSON only.
- **SRQ / STB push events** (server → client unsolicited) — needs
  backend support that doesn't exist yet (Batch D's VXI-11 ADR
  flagged this as v2).
- **Authentication tokens** on the WS endpoint — defers to the same
  Management-API authn ADR.
- **AsyncAPI document** — OpenAPI doesn't describe WebSocket; the
  AsyncAPI equivalent is not auto-generated in v1.
- **`permessage-deflate`** compression — small ASCII SCPI payloads
  don't warrant the CPU cost.
- **Multi-device sessions in one socket** — opening a new connection
  per device is the v1 contract.

## Consequences

- Browser dashboards and AI-agent runtimes have a live SCPI stream on
  the same port as the HTTP control plane.
- No new long-running listener — `ivicli api start` covers both
  transports.
- The WS error code table is the same as the HTTP error code table,
  so client error-handling code is share-able across both.
- Two minimal new types in `IviCli.Api/WebSockets/`; ~280 LoC of
  endpoint logic; no new package dependencies (Web SDK already
  ships `System.Net.WebSockets`).
