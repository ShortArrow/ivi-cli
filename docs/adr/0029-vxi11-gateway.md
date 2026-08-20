# 0029. VXI-11 Gateway

- Status: Accepted
- Date: 2026-05-27

## Context

[ADR 0007 §2](0007-network-transport.md) originally listed VXI-11 as
"investigation only" because of the ONC RPC + XDR dependency. Production
operators now report that real test rigs still ship clients that only
speak VXI-11 — older Keysight VEE installs, the LabVIEW IVI driver,
generic NI-VISA clients that resolve `TCPIP::host::inst0::INSTR` —
and PRD §7.1 already lists VXI-11 second in the Phase 2 priority order.

The HiSLIP gateway ([ADR 0007 §1](0007-network-transport.md) +
[ADR 0007 §1.5](0007-network-transport.md)) proved the `IGatewayServer`
port pattern is the right shape: one bind / port pair, per-connection
task, dispatch into the same `IBackendFactory` everything else uses.
VXI-11 fits the same template; the new surface area is purely the ONC
RPC / XDR wire format plus a portmapper companion.

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

### 2. Out of scope (deferred)

- ~~VXI-11 **Interrupt channel** (program 395185), SRQ delivery.~~
  Shipped in [ADR 0042](0042-vxi11-interrupt-channel.md):
  `device_create_intr_chan` / `device_enable_srq` /
  `device_destroy_intr_chan` on the Core channel, plus an outbound
  `device_intr_srq` from the gateway to the client.
- `device_lock` / `device_unlock` / `device_remote` / `device_local`
  / `device_readstb` / `device_docmd` (procedures 13, 18-20, 22).
  `device_trigger` (proc 17) shipped in
  [ADR 0041](0041-trigger-and-srq-ports.md).
- Vendor extensions, TLS, UDP transport for the Core channel.
  ~~Broadcast portmapper queries on UDP 111~~ — shipped (issue #14):
  the gateway's UDP responder answers them (§1).
- ~~Real **portmapper-at-111** client conversation — Batch D's client
  backend connects directly to the configured Core port instead, since
  the gateway co-locates portmapper + Core on one bind address. v2.~~
  Shipped (issue #20): the client backend now issues a real
  `PMAPPROC_GETPORT` over **UDP/111** to resolve the dynamically-assigned
  Core port of physical instruments (verified against a Kikusui PWR801L,
  whose portmapper answers GETPORT only over UDP and reports a dynamic
  Core port — TCP/111 accepts connections but never replies to GETPORT).
  When no portmapper answers within a short probe window — as with
  ivi-cli's own gateway, which co-locates portmapper + Core on one bind
  address and does not answer GETPORT on 111 — the client falls back to
  the configured fixed port, preserving the gateway pairing. The shared
  GETPORT request/reply codec lives in `Vxi11Portmapper`, reused by the
  broadcast scanner (ADR 0008). A resource may also pin the Core port
  explicitly via the VISA `inst0,<port>` form (ADR 0007 §4); when present
  the client connects there directly and skips the portmapper GETPORT.

The companion client backend (`IviCli.Backends.Vxi11`) shipped in
Batch D, sharing the XDR codec / RPC message records uplifted to
`IviCli.Domain.Protocols` so adding the Interrupt channel later
requires no codec duplication.

### 2a. Abort channel (program 395184)

The abort channel was originally listed under §2 as out-of-scope.
This revision lands a minimum-viable subset because real VXI-11
clients (NI-VISA `viTerminate`, pyvisa-py's `device_abort`) reach
for it as soon as a session blocks long enough to matter — closing
the TCP socket is the only alternative today, and that takes the
whole session with it.

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

- The gateway's link map is now process-wide
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

**Out of scope (still deferred)**

- The Interrupt channel (program 395185 / SRQ delivery). Same
  blocker as HiSLIP Trigger forwarding: no backend port for SRQ
  yet.
- Routing the abort beyond the per-link CTS (e.g. cancelling a
  pending Read response that's already been queued).

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

## Consequences

- Existing VISA tooling that only speaks VXI-11 can drive the same
  routed backend the HiSLIP gateway already serves, completing PRD
  §7.1 priorities 1 + 2 in a consistent UX.
- The XDR codec ships hand-rolled; no third-party RPC NuGet enters the
  graph (ADR 0014 cost-of-deps argument). The codec is small (≈ 200
  LoC) and fully unit-tested at the byte level.
- Out-of-scope procedures (lock, trigger, SRQ) become future
  extensions; the per-connection task structure leaves room to add
  them without touching the wire framing.
- A future `IviCli.Backends.Vxi11` client gets to reuse the codec and
  message records once it lands.
