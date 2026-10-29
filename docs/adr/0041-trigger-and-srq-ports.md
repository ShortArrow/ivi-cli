# 0041. IIviBackend Trigger + ServiceRequest ports

- Status: Accepted

## Context

Two IEEE-488 semantics sit outside write, read and query, and every
transport carries them differently:

- **Trigger**, the `*TRG` equivalent. HiSLIP has a `Trigger` message
  (type 24), VXI-11 has `device_trigger` (proc 17), a VISA session can
  write `*TRG`, and a raw socket has no out-of-band trigger at all.
- **Service Request (SRQ)**, the instrument's push notification when
  its Status Byte requests service. HiSLIP delivers it as
  `ServiceRequest` (type 20) on the async channel, VXI-11 over the
  Interrupt channel, and VISA as a session event.

The gateways ([ADR 0007](0007-network-transport.md),
[ADR 0029](0029-vxi11-gateway.md)) forward a client's trigger to
whatever backend serves the device, and forward that backend's service
requests back to the client. That needs both semantics on the backend
port, in a shape a backend without them can still implement honestly,
and one that passes through the decorator stack (capture, pooling,
instrumentation, device timeout) without each decorator special-casing
it.

## Decision

### 1. Port additions on `IIviBackend`

```csharp
Task<Result<Unit, BackendError>> TriggerAsync(Device, CancellationToken);
IAsyncEnumerable<ServiceRequest> ServiceRequestStream(Device, CancellationToken);
```

- **Trigger**: an async one-shot. A backend that cannot fire a
  trigger returns `BackendError.BackendOperationNotSupported`. The
  Application layer branches on that to surface a clear message
  ("this device doesn't support trigger") without ambiguity around
  `TransportDisconnected`.
- **ServiceRequestStream**: an `IAsyncEnumerable<ServiceRequest>`
  push stream. A backend without SRQ capability yields break
  immediately. The stream lives as long as the supplied
  `CancellationToken`. Multi-consumer semantics are
  backend-specific; the subscribers are the gateway servers (HiSLIP,
  VXI-11, USB/IP), one per client session.

`ServiceRequest` is a value object under `IviCli.Application.Backends`:

```csharp
public sealed record ServiceRequest(
    DeviceName Device,
    byte StatusByte,
    DateTimeOffset Timestamp
);
```

`StatusByte` is best-effort: HiSLIP carries it in the
ServiceRequest message's control byte; synthetic / replayed SRQs
default to zero.

### 2. Implementation matrix

| Backend | TriggerAsync | ServiceRequestStream |
| --- | --- | --- |
| Fake | Counter (`TriggerCountFor`) | `Channel<ServiceRequest>` fed by scenario rules carrying `srq` and by the `RaiseServiceRequest` affordance in tests |
| Replay | `BackendOperationNotSupported` | empty |
| Socket | `BackendOperationNotSupported` | empty |
| Serial | `Write("*TRG")` ([ADR 0055 §6](0055-serial-instruments.md)) | empty |
| ASRL board | the backend the device was opened on ([ADR 0055](0055-serial-instruments.md)) | that backend's stream |
| Local | `Write("*TRG")` via the existing `IVisaSessionHandle` | VISA event queue (§6) |
| HiSlip | Send `Trigger` (type 24) on sync channel | Read `ServiceRequest` (type 20) on async channel |
| VXI-11 | `device_trigger` (proc 17) on Core channel | Interrupt channel ([ADR 0042](0042-vxi11-interrupt-channel.md)) — gateway reverse-connects via `device_intr_srq` |

Decorator chain pass-through:

- **CapturingBackend** emits a `TrafficOp.Trigger` event per call;
  passes `ServiceRequestStream` through transparently (SRQs are
  rare enough that NDJSON tee'ing per event adds little).
- **InstrumentingBackend** emits a `backend.trigger` Activity span
  per call; passes `ServiceRequestStream` through transparently.
- **PoolingBackendProxy** routes Trigger through the leased
  backend (and marks broken on failure); passes the leased
  backend's `ServiceRequestStream` through.
- **DeviceTimeoutBackendFactory** bounds Trigger by the device
  timeout like any other operation; the stream is not bounded, and
  subscribes again after the decorator reopens a timed-out session
  ([ADR 0053](0053-device-timeout.md)).

### 3. HiSLIP wire wiring

**Trigger** (sync channel, type 24):

- Client `HiSlipBackend.TriggerAsync` writes a `Trigger` header
  with `controlCode = 0`, `messageParameter = 0`, no payload, no
  reply expected.
- `HiSlipGatewayServer`'s sync handler calls
  `backend.TriggerAsync(device, ct)`. A failed result, typically
  `BackendOperationNotSupported`, logs at Info instead of tearing
  down the sync channel (matches IVI-6.1 §10.4 "MAY ignore"
  allowance).

**ServiceRequest** (async channel, type 20):

- `HiSlipBackend`'s `OpenAsync` opens a second TCP connection
  for the async channel and sends `AsyncInitialize` with the
  session id captured from `InitializeResponse`. A background
  reader task pulls every async-channel message; type-20
  messages become `ServiceRequest` entries on a per-session
  `Channel<ServiceRequest>`.
- `HiSlipGatewayServer` publishes a `SessionBinding(backend, device)`
  keyed by session id after the sync handshake. The async-
  channel handler spawns `ForwardServiceRequestsAsync` which
  polls `_sessionBindings` for up to 2 s (the client opens
  async ⩽ sync per spec, so it always lands), then subscribes
  to `backend.ServiceRequestStream` and emits `ServiceRequest`
  headers (`controlCode = StatusByte`) to the client.

### 4. VXI-11 wire wiring

- `Vxi11Constants.ProcDeviceTrigger = 17`.
- `Vxi11Backend.TriggerAsync` issues `device_trigger` over the
  Core channel with the standard `Device_GenericParms`.
- `Vxi11GatewayServer.DoDeviceTriggerAsync` decodes the lid,
  resolves the per-link backend, and calls
  `backend.TriggerAsync`. `BackendOperationNotSupported` maps to
  the wire-level `Vxi11NotSupported (8)` so the client sees the
  standard VXI-11 status code.

The service-request side is [ADR 0042](0042-vxi11-interrupt-channel.md).

### 5. Out of scope

- **`IVisaSessionHandle.Trigger()`** as a port method.
  `LocalBackend.TriggerAsync` writes `*TRG`, which works against
  every IEEE-488.2 instrument, so the port has no method invoking
  `IMessageBasedSession.AssertTrigger()`.
- **CapturingBackend NDJSON entry per SRQ.** Capture passes the
  stream through transparently; operators who want SRQs recorded
  tee the stream at the handler level.

### 6. Local backend service requests

The project references `IviFoundation.Visa` directly.
`IVisaSessionHandle` carries `EnableServiceRequests(Action<byte>)`; the
production handle enables the VISA event **queue**
(`EnableEvent(EventType.ServiceRequest)`) and drives a dedicated pump
thread that alternates `WaitOnEvent` slices with `ReadStatusByte()` per
delivered event.

The CLR `ServiceRequest` event is not used: its add accessor arms VISA's
handler mechanism, which NI-VISA rejects for service requests on USB
sessions (verified against NI-VISA with a USBTMC instrument).

`LocalBackend.ServiceRequestStream` enables the subscription on first
consumption and drains an unbounded `Channel<ServiceRequest>`. Delivery
is best-effort: no open session or a refused enable yields an empty
stream, and a status byte that cannot be read drops that one SRQ rather
than killing the pump.

## Consequences

- HiSLIP and VXI-11 clients can trigger an instrument through the
  gateway and observe its SRQ notifications end-to-end, whichever
  backend serves the device.
- Every `IIviBackend` implements both methods, and every decorator
  passes them through, so a new backend states its trigger and SRQ
  support in the two methods rather than in a capability flag.
- `BackendOperationNotSupported` extends the `BackendError` sum
  with a distinct shape so the Management API + CLI surface
  layers can render a clear error per ADR 0014.
- A backend without service requests looks the same to a subscriber
  as an instrument that never raises one: the stream is empty.

## Verification

- `dotnet test --filter "Category!=Integration"` covers:
  - Fake `TriggerCountFor` + `RaiseServiceRequest` round trip.
  - HiSLIP client → gateway → Fake `TriggerAsync` and SRQ
    propagation.
  - VXI-11 wire-level `device_trigger` → Fake `TriggerCountFor`.
- Manual: HiSLIP client (NI-VISA's `viAssertTrigger` /
  `viInstallHandler(VI_EVENT_SERVICE_REQ, ...)`) against
  `ivicli server start` lands at the Fake / Local backend.
- Local backend: the `LocalBackendUsbMockBenchTests` pair (gated on
  `[Requires("ni-visa", "usb-mock")]`) runs against the virtual USB
  mock (ADR 0049) attached through usbip-win2 and NI-VISA. The
  IEEE 488.2 sequence delivers the scenario rule's status byte 0x60
  through `ServiceRequestStream`, and `TriggerAsync`'s `*TRG` raises
  the distinct 0x41, so the trigger leg is told apart from the
  completing operation.
