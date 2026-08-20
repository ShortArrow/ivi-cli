# 0041. IIviBackend Trigger + ServiceRequest ports

- Status: Accepted
- Date: 2026-05-28

## Context

Batch L ([ADR 0007 §1.6](0007-network-transport.md) +
[ADR 0029 §2](0029-vxi11-gateway.md)) landed protocol-depth wiring
for HiSLIP v3 and the VXI-11 abort channel, but deliberately
deferred two semantic features:

- **Trigger** (the IEEE-488 / SCPI `*TRG` equivalent). HiSLIP
  message type 24 was added to the enum but the gateway server
  responded with an Info-level no-op log because `IIviBackend`
  had no `TriggerAsync` port. VXI-11 `device_trigger` (proc 17)
  was simply unrouted.
- **Service Request (SRQ).** HiSLIP `ServiceRequest` (type 20) is
  the spec'd push notification when the instrument raises its
  Status Byte. Batch L noted v2 SRQ depth without committing a
  contract.

The pool / capture / instrumentation work since then (Batches M / O)
made it safe to land these as cross-cutting decorator behaviour —
the new ports thread through the existing factory stack cleanly.

## Decision

### 1. Port additions on `IIviBackend`

```csharp
Task<Result<Unit, BackendError>> TriggerAsync(Device, CancellationToken);
IAsyncEnumerable<ServiceRequest> ServiceRequestStream(Device, CancellationToken);
```

- **Trigger**: an async one-shot. Backends that cannot fire a
  hardware trigger return the new
  `BackendError.BackendOperationNotSupported` variant. The
  Application layer can branch on that to surface a clear
  message ("this device doesn't support trigger") without
  ambiguity around `TransportDisconnected`.
- **ServiceRequestStream**: an `IAsyncEnumerable<ServiceRequest>`
  push stream. Backends without SRQ capability yield break
  immediately. The stream lives as long as the supplied
  `CancellationToken`. Multi-consumer semantics are
  backend-specific — in v1 only the gateway server subscribes
  per device.

`ServiceRequest` is a new value object under
`IviCli.Application.Backends`:

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

### 2. Implementation matrix (v1)

| Backend | TriggerAsync | ServiceRequestStream |
| --- | --- | --- |
| Fake | Counter (`TriggerCountFor`) | `Channel<ServiceRequest>` fed by scenario rules carrying `srq` and by the `RaiseServiceRequest` affordance in tests |
| Replay | `BackendOperationNotSupported` | empty |
| Socket | `BackendOperationNotSupported` | empty |
| Local | `Write("*TRG")` via existing IVisaSessionHandle | `IMessageBasedSession.ServiceRequest` event via `IVisaSessionHandle.EnableServiceRequests` |
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

### 3. HiSLIP wire wiring

**Trigger** (sync channel, type 24):

- Client `HiSlipBackend.TriggerAsync` writes a `Trigger` header
  with `controlCode = 0`, `messageParameter = 0`, no payload, no
  reply expected.
- `HiSlipGatewayServer`'s sync handler replaces the Batch L
  no-op log with `await backend.TriggerAsync(device, ct)`. A
  `BackendOperationNotSupported` result logs at Info instead of
  tearing down the sync channel (matches IVI-6.1 §10.4 "MAY
  ignore" allowance).

**ServiceRequest** (async channel, type 20):

- `HiSlipBackend`'s `OpenAsync` now opens a second TCP connection
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

### 5. Out of scope (v1)

- ~~**VXI-11 Interrupt channel (program 395185)**.~~ Shipped in
  [ADR 0042](0042-vxi11-interrupt-channel.md). The Interrupt channel
  inverts the TCP direction — the gateway connects out to a port
  the client listens on. Both sides land together with codec +
  forwarder + accept loop + e2e test.
- **`IVisaSessionHandle.Trigger()`** as a first-class port method.
  v1 maps `LocalBackend.TriggerAsync` to `Write("*TRG")` —
  works against every IEEE-488.2 instrument. Adding a reflection-
  invoked `IMessageBasedSession.AssertTrigger()` is a Tidy-First
  refactor that pairs naturally with Ivi.Visa event subscription
  for SRQ.
- ~~**Reflection-based SRQ subscription on the Local backend.**~~
  Superseded: the project references `IviFoundation.Visa` directly,
  so no reflection is involved. `IVisaSessionHandle` carries
  `EnableServiceRequests(Action<byte>)`; the production handle enables
  the VISA event **queue** (`EnableEvent(EventType.ServiceRequest)`)
  and drives a dedicated pump thread that alternates `WaitOnEvent`
  slices with `ReadStatusByte()` per delivered event. The CLR
  `ServiceRequest` event is deliberately not used: its add accessor
  arms VISA's handler mechanism, which NI-VISA rejects for service
  requests on USB sessions (verified against NI-VISA with a USBTMC
  instrument). `LocalBackend.ServiceRequestStream` enables the
  subscription on first consumption and drains an unbounded
  `Channel<ServiceRequest>`. Delivery stays best-effort: no open
  session or a refused enable yields an empty stream, and a status
  byte that cannot be read drops that one SRQ rather than killing
  the pump.
- **CapturingBackend NDJSON entry per SRQ.** Capture passes the
  stream through transparently; if operators want SRQs recorded
  they tee the stream at the handler level.

## Consequences

- HiSLIP clients can now trigger an instrument through the gateway
  and observe its SRQ notifications end-to-end — the path
  client → gateway → FakeBackend (or Local backend) is
  exercised by the new tests.
- The `IIviBackend` port stays sealed: existing decorators (capture,
  pool, instrumenting) updated in one pass; future backends (e.g.
  USB / GPIB v2 native) implement the two new methods up front.
- `BackendOperationNotSupported` extends the `BackendError` sum
  with a distinct shape so the Management API + CLI surface
  layers can render a clear error per ADR 0014.
- ~~VXI-11 Interrupt channel is a known limitation, documented here~~
  resolved in [ADR 0042](0042-vxi11-interrupt-channel.md)
  and pointed to from ADR 0029.

## Verification

- `dotnet test --filter "Category!=Integration"` covers:
  - Fake `TriggerCountFor` + `RaiseServiceRequest` round trip.
  - HiSLIP client → gateway → Fake `TriggerAsync` and SRQ
    propagation.
  - VXI-11 wire-level `device_trigger` → Fake `TriggerCountFor`.
- Manual: HiSLIP client (NI-VISA's `viAssertTrigger` /
  `viInstallHandler(VI_EVENT_SERVICE_REQ, ...)`) against
  `ivicli server start` lands at the Fake / Local backend.
- Local backend, observed end-to-end (2026-08-20): the
  `LocalBackendUsbMockBenchTests` pair (gated on
  `[Requires("ni-visa", "usb-mock")]`) runs against the virtual USB
  mock (ADR 0049) attached through usbip-win2 and NI-VISA. The
  IEEE 488.2 sequence delivers the scenario rule's status byte 0x60
  through `ServiceRequestStream`, and `TriggerAsync`'s `*TRG` raises
  the distinct 0x41 — so the trigger leg is told apart from the
  completing operation. Runs are recorded on issue #18.
