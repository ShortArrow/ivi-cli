# 0007. Network Transport

- Status: Accepted
- Date: 2026-05-23

## Context

PRD §7 sets the strategy for Phase 2: the project exposes local VISA
resources as a remote instrument gateway that existing VISA clients can
connect to. The priority list in PRD §7.1 is unambiguous:

1. HiSLIP-compatible server
2. VXI-11-compatible server
3. Raw TCP Socket endpoint
4. IVI-CLI management API

ADRs 0010 (DI) and 0014 (errors) already declared the cross-layer
shape (port + adapter + Result-based error mapping). What this ADR
fills in is the **transport-by-transport scope and threading sketch**
for Phase 2 — what we build, what we defer, and what each protocol
must guarantee at the wire level.

## Decision

### 1. Phase 2 in-scope transports

- **Raw TCP SOCKET** — implemented first; simplest wire format
  (line-oriented SCPI terminated by `\n`), no out-of-band control,
  no locking, no SRQ. Most useful for ad-hoc scripts and CI runners.
- **HiSLIP** — implemented second; the protocol existing VISA clients
  reach for when they see a `TCPIP::host::hislip0::INSTR` resource.
  The minimum-viable subset is: Initialize / Maximum-Message-Size /
  synchronous channel + Data + DataEnd / AsyncInitialize for the
  control channel / FatalError. Locking, async I/O cancellation,
  trigger, and remote-mode handling are out of scope for v1.

### 1.5 HiSLIP v2 — operator features (this revision)

HiSLIP v1 above covers the happy-path SCPI tunnel. HiSLIP v2 adds the
operator-facing controls real VISA clients exercise as soon as a
session lives long enough to matter. Message-type values match
**IVI-6.1 §10**'s table verbatim so real VISA clients (NI, Keysight,
R&S, PyVISA) interoperate at the wire level:

- **Async device clear** — the spec's recommended way to reset the
  bound instrument's I/O buffers without tearing the session down.
  Message types `AsyncDeviceClear` (19) and
  `AsyncDeviceClearAcknowledge` (23). The server flushes the
  per-session sync-channel read/write state and replies on the async
  channel.
- **Async lock / release** — exclusive access negotiation. A single
  message type `AsyncLock` (4) handles both acquire and release: the
  header's control byte is `1` to acquire and `0` to release. The
  server replies with `AsyncLockResponse` (5) carrying `1` for granted
  / `0` for denied. The server tracks a per-route lock holder (single
  session id); contended acquire returns denied. Locks are released
  automatically on disconnect.
- **Service Request (SRQ)** — server → client notification on the
  async channel that the underlying instrument raised its STB bit.
  Message type `ServiceRequest` (20). v2 implemented the framing;
  v3.1 ([ADR 0041](0041-trigger-and-srq-ports.md)) wires the
  gateway to subscribe to `IIviBackend.ServiceRequestStream` and
  forward each event as a `ServiceRequest` message (StatusByte in
  the control byte).

TLS, vendor extensions, the remote/local control pair, and async
status query remain deferred. Lock-timeout semantics and the trigger
sub-protocol land in v3 (§1.6 below).

#### Spec values (IVI-6.1 §10)

| Type | Value |
| --- | --- |
| Initialize | 0 |
| InitializeResponse | 1 |
| FatalError | 2 |
| Error | 3 |
| AsyncLock | 4 |
| AsyncLockResponse | 5 |
| Data | 6 |
| DataEnd | 7 |
| DeviceClearComplete | 8 |
| DeviceClearAcknowledge | 9 |
| AsyncMaximumMessageSize | 15 |
| AsyncMaximumMessageSizeResponse | 16 |
| AsyncInitialize | 17 |
| AsyncInitializeResponse | 18 |
| AsyncDeviceClear | 19 |
| ServiceRequest | 20 |
| AsyncDeviceClearAcknowledge | 23 |
| Trigger | 24 |

Values 10–14, 21–22, 25–26 are reserved by the spec for messages we
defer (remote/local control, async interrupted, async status query,
lock info) and intentionally absent from `HiSlipMessageType` so an
enum cast to one of those bytes cannot silently succeed.

### 1.6 HiSLIP v3 — protocol-depth additions (this revision)

v3 adds two of the items v2 deferred. Together they remove the most
common reasons a real VISA client falls back to FatalError or to
unreliable polling against this gateway:

- **Lock timeout** — `AsyncLock` (acquire) requests carry the lock
  timeout in milliseconds in the header's MessageParameter field
  (IVI-6.1 §10). v1 / v2 ignored that field and rejected immediately
  under contention. v3 polls the per-server lock holder every 50 ms
  until the deadline expires and grants the lock on the first
  observed release. `lock_timeout == 0` keeps the original
  immediate-deny behaviour, so clients that don't ask to wait are
  unaffected. Polling is honest about the per-server scope; a
  signal-based release notification can land in v4 if it earns its
  weight.
- **Trigger** — message type `Trigger` (24, sync channel). v1 / v2
  treated it as unsupported and tore the connection down with
  FatalError. v3 accepts the message; v3.1
  ([ADR 0041](0041-trigger-and-srq-ports.md)) adds an
  `IIviBackend.TriggerAsync` port and the gateway now forwards
  the message to it. `BackendOperationNotSupported` results log at
  Info instead of teaching down the sync channel — matches the
  spec's "MAY ignore" allowance in §10.4.

TLS, vendor extensions, the remote/local control pair, async status
query, and the lock-string-based contention model remain deferred
to a future v4 ADR.

### 2. Phase 2 deferred (future ADRs / revisions)

- **VXI-11** — moved to [ADR 0029](0029-vxi11-gateway.md). Wire-level
  subset (Core program 395183/v1 + portmapper GETPORT) lands so legacy
  VISA clients on a `TCPIP::host::inst0::INSTR` resource can drive the
  same routed backend the HiSLIP gateway already serves. The abort
  channel (program 395184) lands with §2a of ADR 0029.
- **Management API** (gRPC / HTTP JSON) — declared in PRD §7.5; the
  surface is out of scope for Phase 2 v1. When it lands, it gets its
  own ADR (0019 area).
- **HiSLIP v4** — TLS wrap, lock-string semantics, remote/local
  control, async status query, vendor extension messages.

### 3. Wire-format guarantees per transport

| Transport | Read termination | Write termination | Char set | Errors |
| --- | --- | --- | --- | --- |
| SOCKET | LF (`\n`); optional CRLF tolerated | LF (`\n`) appended | UTF-8 input passed through; output bytes as-supplied | Connection close on fatal |
| HiSLIP | MessageHeader length-prefixed body | MessageHeader length-prefixed body | ASCII for control messages; payload bytes opaque | FatalError control message + close |

### 4. Bind defaults

- Default bind: `127.0.0.1` (loopback) for both protocols. PRD §17
  (security) treats Phase 2 as "trusted LAN" but defaulting to loopback
  forces operators to opt in to LAN exposure with an explicit
  `--bind 0.0.0.0`.
- Default ports:
  - SOCKET: `5025` (industry-standard SCPI/raw socket port).
  - HiSLIP: `4880` (IVI Foundation registered port).

### 5. TLS / authentication

Phase 2 v1 ships **plaintext only**. ADR 0017 §6 marked TLS + mTLS as
the Phase 2 baseline; the present revision narrows that to "deferred
to a follow-up ADR after the gateway functionality exists" because the
HiSLIP specification itself does not include TLS — wrapping HiSLIP in
TLS is a deviation from the spec that VISA clients may or may not
handle. The follow-up ADR will decide between (a) TLS-wrap behind a
distinct port, (b) a wholly proprietary TLS-mode handshake, or
(c) restricting TLS to the Management API and leaving HiSLIP/SOCKET on
trusted LAN. Operators are expected to deploy on a trusted LAN until
that ADR lands.

### 6. Listener / connection model

- One `TcpListener` per protocol instance.
- Per-incoming-connection `Task` for connection handling (see ADR 0015
  for the threading rules).
- Backpressure: no explicit limit in v1; operating-system socket
  backlog absorbs surges. A `--max-connections` knob is a follow-up.
- Graceful shutdown: `CancellationToken` flows from
  `IGatewayServer.StopAsync` through to every connection task; each
  task wraps its current operation in `try` /
  `catch (OperationCanceledException)` and closes its socket cleanly.

### 7. Resource binding at runtime

`server route add hislip0 psu1` binds the public endpoint name
`hislip0` (or `5025` for SOCKET) to the locally-configured device
`psu1`. At connect time, the gateway:

1. Reads the resource name the client connected to.
2. Looks the public-endpoint name up in the active `ConfigDocument.Routes`.
3. Loads the bound `Device`.
4. Resolves an `IIviBackend` for that device via `IBackendFactory`
   (ADR 0010 §4).
5. Forwards SCPI to that backend; pipes responses back.

The gateway therefore does not assume the bound device lives on the
local NI-VISA install — anything that resolves to an `IIviBackend`
works, including chained `HiSlipBackend` instances (a proxy
configuration future-proofed by this design).

### 8. Backwards compatibility with existing VISA clients

Both protocols must be byte-identical to the relevant standard for
common operations:

- A SOCKET client running `python -c "s.send(b'*IDN?\n'); print(s.recv(4096))"`
  must work.
- A HiSLIP client (NI-VISA, R&S VISA, PyVISA) connecting to
  `TCPIP0::host::hislip0::INSTR` and sending `*IDN?` must receive the
  same response payload as a direct LAN connection to the bound
  instrument.

Compliance with PyVISA / NI-VISA is the acceptance-test bar; ad-hoc
extensions are forbidden in v1.

### 9. Error-path mapping

`BackendError` variants emerging from the local backend are folded
into protocol-native error reporting:

- SOCKET: connection close + an Information-level log entry.
- HiSLIP: a `FatalError` control message describing the variant, then
  connection close.

The control-plane Management API will surface the same `BackendError`
sum directly when it lands.

### 10. Observability touch-points

Per ADR 0011 §10, each accepted connection opens a logging scope
`{ Protocol, RemoteEndpoint, RouteEndpoint }`. Phase 2 v1 logs at
Information for connect / disconnect and Debug for each SCPI exchange;
structured fields stay machine-parseable.

## Consequences

**Pros**

- The protocol priority matches PRD §7.1, so existing VISA clients
  reach the gateway naturally.
- Phase 2 v1 is *useful* with SOCKET alone (simple lab automation) and
  *interoperable* with HiSLIP for VISA-native callers.
- TLS being deferred keeps the v1 surface small; operators with
  off-LAN needs have a documented escape (tunnel through SSH / wrap
  via stunnel) until the follow-up ADR.

**Cons**

- HiSLIP without locking / SRQ / async-IO cancellation is a notable
  subset of the spec — sophisticated clients (those using `viLock` or
  service-request callbacks) will hit "not implemented" errors.
- The plaintext-only stance is a security-policy gap with ADR 0017 §6
  until the follow-up TLS ADR lands; we explicitly call it out as a
  known limitation.

**Mitigations**

- Unsupported HiSLIP features return a HiSLIP `FatalError` with a
  clear reason rather than silent no-ops.
- Loopback default (`127.0.0.1`) keeps the v1 footprint inside the host
  unless the operator opts in. README and `server start --help` call
  this out.
- A follow-up ADR is tracked for the TLS decision.
