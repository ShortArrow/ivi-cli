# 0029. VXI-11 Gateway

- Status: Accepted

## Context

Real test rigs still run clients that only speak VXI-11 — older
Keysight VEE installs, the LabVIEW IVI driver, generic NI-VISA clients
that resolve `TCPIP::host::inst0::INSTR` — and PRD §7.1 lists VXI-11
second in the Phase 2 priority order. VXI-11 rests on ONC RPC and XDR,
which no other transport in ivi-cli speaks.

The HiSLIP gateway ([ADR 0007 §1](0007-network-transport.md) +
[ADR 0007 §1.5](0007-network-transport.md)) has the `IGatewayServer`
port shape: one bind / port pair, per-connection task, dispatch into the
same `IBackendFactory` everything else uses. VXI-11 fits the same
template; what it adds is the ONC RPC / XDR wire format and a portmapper
companion.

## Decision

### 1. In-scope wire surface

The gateway implements the **VXI-11 Core channel** (program 395183,
version 1) and only the RPC procedures required to satisfy PRD §6.2 —
`open`, `write`, `query`, `read`, `status`:

| Procedure | Number | Purpose |
| --- | --- | --- |
| `create_link` | 10 | session open → assigns link id |
| `device_write` | 11 | SCPI write |
| `device_read` | 12 | SCPI read |
| `device_clear` | 14 | flush per-session buffers |
| `destroy_link` | 23 | session close |

Plus a co-located **portmapper** (program 100000, version 2) that
implements `PMAPPROC_GETPORT` (procedure 3) so clients can resolve the
Core program to the gateway's TCP port without a system rpcbind. The
portmapper and Core channel share one process and one bind address —
the portmapper listens on the same port the user configures for the
server, advertises that same port for the Core program, and dispatches
RPC calls to the Core handler when the program number matches. A
best-effort **UDP responder** (issue #14) additionally answers GETPORT
datagrams on UDP 111 — the transport the broadcast scanner probes and
unicast portmap clients (ivicli's own backend included) use. When the
bind fails (elevation on Unix, a resident rpcbind) the gateway logs it
and runs on; the TCP portmap path is unaffected.

### 2. Out of scope

- `device_lock` / `device_unlock` / `device_remote` / `device_local`
  / `device_readstb` / `device_docmd` (procedures 13, 18-20, 22).
- Vendor extensions, TLS, UDP transport for the Core channel.

Beyond the Core procedures above, the abort channel is §2a,
`device_trigger` (procedure 17) is
[ADR 0041](0041-trigger-and-srq-ports.md), and the Interrupt channel
(program 395185) with SRQ delivery is
[ADR 0042](0042-vxi11-interrupt-channel.md).

### 2a. Abort channel (program 395184)

The gateway implements a minimum-viable subset of the abort channel,
because real VXI-11 clients (NI-VISA `viTerminate`, pyvisa-py's
`device_abort`) reach for it as soon as a session blocks long enough to
matter. Without it the only way to stop a blocked operation is closing
the TCP socket, which takes the whole session with it.

**Wire surface**

- Program: `395184`. Version: `1`. Procedure: `device_abort` (1).
- Argument: `Device_Link { i32 lid }`. Response:
  `Device_Error { i32 error }`.
- Port co-location: the abort channel shares the bound TCP port with
  the Core channel and the co-located portmapper. The portmapper's
  `PMAPPROC_GETPORT` returns the same port number for both
  `CoreProgram (395183)` and `AbortProgram (395184)`. The `create_link`
  reply's `abort_port` field advertises that same port.
- Transport: clients open a SECOND TCP connection to that port and
  send the abort RPC there, mirroring the IVI-6.2 contract.

**Behaviour**

- The gateway's link map is process-wide
  (`ConcurrentDictionary<int, LinkState>` on the gateway instance)
  so abort traffic on a separate connection can find the target
  link. Each connection still tracks a `HashSet<int>` of lids it
  owns, so disconnect teardown only closes its own sessions.
- Each `LinkState` carries a `CancellationTokenSource`. On
  `device_abort` for a known lid: the CTS is cancelled and the
  reply returns `NO_ERROR (0)`. On an unknown lid:
  `INVALID_LINK_IDENTIFIER (4)`.
- Operation paths (`device_write`'s backend `QueryAsync` /
  `WriteAsync` calls) take a token linked from the connection token
  and the link's per-link CTS. A polite backend stops promptly on
  abort; a backend that ignores its token will only stop when its
  current operation finishes — we test the protocol contract, not
  the backend's cancellation fidelity.

**Out of scope**

- Routing the abort beyond the per-link CTS (e.g. cancelling a
  pending Read response that's already been queued).

### 2b. Client backend

`IviCli.Backends.Vxi11` is the client side of the same protocol. It
shares the XDR codec and RPC message records in
`IviCli.Domain.Protocols` with the gateway, and the GETPORT
request/reply codec in `Vxi11Portmapper` with the broadcast scanner
(ADR 0008).

- **Core port resolution.** On open the client issues
  `PMAPPROC_GETPORT` over **UDP/111** for the Core program. Physical
  instruments assign the Core port dynamically, and some answer GETPORT
  only over UDP: the Kikusui PWR801L accepts TCP/111 connections but
  never replies to GETPORT on them.
- **Fallback.** When no portmapper answers within a short probe window,
  the client connects to the configured fixed port. That keeps the
  pairing with an ivi-cli gateway whose UDP responder could not bind
  port 111 (§1).
- **Pinned port.** A resource may pin the Core port with the VISA
  `inst0,<port>` form (ADR 0007 §4); the client then connects there
  directly and skips GETPORT.

### 3. Wire-format guarantees

- **Record marking (RFC 1831 §10):** every fragment is preceded by a
  4-byte big-endian header where the high bit is `LAST_FRAGMENT` and
  the low 31 bits are the fragment length. v1 always sends single-
  fragment messages.
- **XDR (RFC 4506):** all integers are 4-byte big-endian, two's
  complement; strings and opaque payloads are length-prefixed and
  padded to a 4-byte boundary with zero bytes.
- **RPC envelope (RFC 1831 §9):** the gateway accepts only
  `mtype = CALL (0)`, `rpcvers = 2`, `cred = verf = AUTH_NONE (0, 0)`,
  and replies with `MSG_ACCEPTED (0)` + `SUCCESS (0)`. Any other shape
  returns the relevant `MSG_DENIED` / `PROG_MISMATCH` /
  `PROC_UNAVAIL` reject and closes the connection.
- **SCPI termination:** writes use the data bytes as-is; reads return
  the backend's response unchanged. The Core RPC `flags` field's
  `end` and `termcharset` bits are honoured well enough to interoperate
  with NI-VISA's default behaviour (which always sets `end = 1` on the
  last fragment and reads up to the terminator).

### 4. Routing & lifecycle

- One configured `Server` row of `ServerType.Vxi11` corresponds to one
  TCP listener. The first matching `Route` selects the device, exactly
  like the HiSLIP gateway. v1 exposes a single logical instrument per
  server.
- `create_link` opens the backend session and returns a fresh link id
  (`int` issued by an `Interlocked.Increment` counter). Subsequent
  calls validate the link id; mismatches return
  `VXI11_ERROR_INVALID_LINK_IDENTIFIER (4)`.
- Per-session state lives in the per-connection task; cancellation of
  the gateway's `RunAsync` token tears down accepted connections
  cleanly.

### 5. Error mapping

VXI-11 wire errors are a flat 32-bit table; v1 uses the canonical
subset:

| Wire code | Constant | When |
| --- | --- | --- |
| 0 | `NO_ERROR` | success |
| 1 | `SYNTAX_ERROR` | malformed RPC body |
| 4 | `INVALID_LINK_IDENTIFIER` | unknown / closed link |
| 8 | `OPERATION_NOT_SUPPORTED` | procedure not implemented |
| 15 | `IO_TIMEOUT` | backend timeout |
| 17 | `IO_ERROR` | backend error |

The gateway logs the backend error behind a failed write or query, at the error's
own severity, so the log says why the call failed.

## Consequences

- Existing VISA tooling that only speaks VXI-11 can drive the same
  routed backend the HiSLIP gateway already serves, completing PRD
  §7.1 priorities 1 + 2 in a consistent UX.
- The XDR codec ships hand-rolled; no third-party RPC NuGet enters the
  graph (ADR 0014 cost-of-deps argument). The codec is small (≈ 200
  LoC) and fully unit-tested at the byte level.
- The per-connection task structure leaves room to add the
  out-of-scope procedures (§2) without touching the wire framing.
- The client backend (§2b) reuses the gateway's codec and message
  records rather than carrying its own.
