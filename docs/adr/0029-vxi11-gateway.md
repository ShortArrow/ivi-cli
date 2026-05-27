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
RPC calls to the Core handler when the program number matches.

### 2. Out of scope (deferred)

- VXI-11 **Abort channel** (program 395184) and async cancellation.
- VXI-11 **Interrupt channel** (program 395185), SRQ delivery.
- `device_lock` / `device_unlock` / `device_trigger` / `device_remote`
  / `device_local` / `device_readstb` / `device_docmd` (procedures 13,
  15–20, 22).
- Vendor extensions, TLS, UDP transport, broadcast portmapper queries
  on UDP 111.
- Real **portmapper-at-111** client conversation — Batch D's client
  backend connects directly to the configured Core port instead, since
  the gateway co-locates portmapper + Core on one bind address. v2.

The companion client backend (`IviCli.Backends.Vxi11`) shipped in
Batch D, sharing the XDR codec / RPC message records uplifted to
`IviCli.Domain.Protocols` so adding the Abort or Interrupt channels
later requires no codec duplication.

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
- Out-of-scope procedures (lock, trigger, abort, SRQ) become future
  extensions; the per-connection task structure leaves room to add
  them without touching the wire framing.
- A future `IviCli.Backends.Vxi11` client gets to reuse the codec and
  message records once it lands.
