# 0042. VXI-11 Interrupt channel (program 395185)

- Status: Accepted

## Context

HiSLIP clients of ivi-cli's gateway receive SRQ events end-to-end. A
VXI-11 client of the same gateway expects them too, through the VXI-11
Interrupt channel, and `Vxi11Backend.ServiceRequestStream` has no other
source for them.

The protocol shape is unusual: the Interrupt channel is the only
VXI-11 RPC where the TCP direction is **reversed** — the server
opens an outbound connection to a port the client is listening on.
Neither side can be tested without the other, so the gateway and the
client backend implement it together and one end-to-end test covers
both (Verification).

## Decision

### 1. Wire surface

The gateway implements three Core-channel RPCs and one outbound RPC
on the Interrupt program:

| RPC | Program | Proc | Direction | Args | Reply |
| --- | --- | --- | --- | --- | --- |
| `device_create_intr_chan` | Core (395183) | 25 | client → server | `Device_RemoteFunc` | `Device_Error` |
| `device_destroy_intr_chan` | Core (395183) | 26 | client → server | empty | `Device_Error` |
| `device_enable_srq` | Core (395183) | 18 | client → server | `Device_EnableSrqParms { lid, enable, handle }` | `Device_Error` |
| `device_intr_srq` | Interrupt (395185) | 30 | **server → client** | `Device_SrqParms { handle }` | empty |

The `progFamily` field of `Device_RemoteFunc` must be `TCP (6)`
and the program must be `395185 / 1` — anything else returns
`OPERATION_NOT_SUPPORTED (8)`. UDP is not supported.

### 2. Setup sequence (per session)

```
Client                                Gateway
  │                                     │
  ├── create_link ────────────────────►│
  │◄── lid ────────────────────────────┤
  │                                     │
  ├── (bind TCP listener on port P)    │
  │                                     │
  ├── device_create_intr_chan(host,P)─►│ store target in ConnectionInterruptState
  │◄── NoError ────────────────────────┤
  │                                     │
  ├── device_enable_srq(lid,true,h)──►│ start per-link forwarder
  │◄── NoError ────────────────────────┤
  │                                     │
  │       <SRQ event>                   │
  │                                     │ backend.ServiceRequestStream yields
  │◄═══ TCP connect to (host,P) ═══════┤
  │◄═══ device_intr_srq(h) ════════════┤
  │═══ empty reply ════════════════════►│
  │                                     │
  ├── device_enable_srq(lid,false)──►  │ stop forwarder
  ├── device_destroy_intr_chan ──────► │ clear target
  ├── destroy_link ─────────────────►  │
  │                                     │
```

The handle byte sequence carried by `device_enable_srq` is echoed
back by every `device_intr_srq` so the client can correlate a
delivery with a specific enable call. The client uses 4 random bytes per
session.

### 3. Server-side design

`Vxi11GatewayServer` adds:

- A connection-scoped `ConnectionInterruptState` holding the
  `Target` (the most recent `Device_RemoteFunc` from
  `device_create_intr_chan`) and `ForwardingLinks` (a HashSet&lt;int&gt;
  tracking which lids on this connection have an active forwarder).
- A per-link `SrqForwarder` task started by
  `device_enable_srq(enable=true)`. The task subscribes to
  `backend.ServiceRequestStream(state.Device, token)` and on each
  yield TCP-connects to the connection's target host:port,
  encodes `device_intr_srq(handle)` as a CALL with the
  Interrupt program / version / procedure constants, and writes
  it on that TCP connection.
- Delivery failures (rogue client port, refused connection, etc.)
  log at Warning and drop the SRQ. The forwarder loop continues so
  later events still try. The alternative — tearing down the
  whole session on first SRQ-delivery failure — would surprise
  operators who closed only the SRQ listener.

`LinkState.StartSrqForwarder` / `StopSrqForwarder` own the
CancellationTokenSource + Task; `Dispose` cancels and joins so
the gateway shuts down cleanly.

### 4. Client-side design

`Vxi11Backend` `OpenAsync`:

1. Calls `CreateLinkAsync`.
2. Calls `Vxi11Session.StartInterruptListener` which binds a
   loopback TCP listener on a free port (the OS picks it). The
   listener's address is encoded as a uint32 (network byte order
   per the VXI-11 spec; we encode big-endian internally).
3. Calls `CreateIntrChanAsync` with that host/port + the
   Interrupt program constants.
4. Calls `DeviceEnableSrqAsync(enable=true, handle=session.InterruptHandle)`.
5. The listener accepts inbound TCP from the gateway. Each
   accepted connection runs an inner loop that reads ONC RPC
   records, decodes the `device_intr_srq` body, pushes a
   `ServiceRequest` entry onto the session's `Channel<ServiceRequest>`,
   and writes an empty MSG_ACCEPTED reply so the server can drain.

`ServiceRequestStream` yields from the channel until the supplied
`CancellationToken` trips. The stream completes silently when
`InterruptSetupFailed` (e.g. the gateway rejected our RemoteFunc)
or the session disposes.

`CloseAsync` sends `device_enable_srq(false)` + `destroy_intr_chan`
before `destroy_link` so the gateway's forwarder stops cleanly.

### 5. Out of scope

- **Multi-handle per link.** The gateway stores one handle per link; calling
  `device_enable_srq` a second time replaces the prior handle. The
  spec allows multiple concurrent handles via separate enable calls
  but no client we've seen uses that.
- **`progFamily = UDP`.** `device_intr_srq` is delivered over TCP
  only.
- **Loopback assumption.** The client's listener binds to
  `127.0.0.1`. Cross-host VXI-11 (gateway and client on
  different machines) is not supported: the gateway would have to
  dial across the network.
- **TLS on the Interrupt channel.** The Core / Abort channels
  share whatever transport-security stance ADR 0017 / 0039
  publishes. Interrupt rides plaintext TCP, as VXI-11 has no TLS in
  ivi-cli (ADR 0029 §2).
- **Reconnect on transient delivery failure.** A failed delivery
  drops the SRQ and the forwarder continues (§3).

## Consequences

- `Vxi11Backend` has feature parity with `HiSlipBackend` for SRQ
  delivery.
- The Core handler `LinkState` gains lifecycle hooks
  (StartSrqForwarder / StopSrqForwarder) that future RPCs needing
  per-link background work can reuse.
- Cross-process control surface grows by one TCP listener per
  `Vxi11Backend` session. Operators behind tight firewall rules
  need to allow the ephemeral inbound port.
- `Vxi11InterruptCodec` houses the encode/decode pair for
  `Device_RemoteFunc`, `Device_EnableSrqParms`, `Device_SrqParms`.
  Both the gateway and the client backend reference it so the
  wire shape stays in one place.

## Verification

- `dotnet test --filter "Category!=Integration"` includes:
  - Codec round-trip for the three XDR structures.
  - End-to-end: `Fake.RaiseServiceRequest → Vxi11GatewayServer
    forwarder → Vxi11Backend client → ServiceRequestStream yields`.
- Manual: pyvisa-py / NI-VISA against `ivicli server start`
  observes `viInstallHandler(VI_EVENT_SERVICE_REQ, ...)` firing
  on FakeBackend / Local backend SRQs.
