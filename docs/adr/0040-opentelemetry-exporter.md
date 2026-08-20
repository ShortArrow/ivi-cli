# 0040. OpenTelemetry exporter

- Status: Accepted
- Date: 2026-05-28

## Context

ADR 0011 declared structured logging via Serilog and explicitly
**deferred** the broader observability story (traces, metrics, OTel
export) until "we have something long-running worth tracing." That
moment arrived three batches ago:

- **`ivicli api start` / `ivicli server start ...`** are now common
  long-running processes that operators leave up for days.
- **Batch M's session pool** has internal state (cached entries,
  lease waits, evictions) that operators want to monitor without
  parsing log files.
- **AI-agent integrations** of the Management API benefit from
  per-request spans that propagate trace context end-to-end.

Serilog itself ships an OTel sink, but logs + traces + metrics is a
single observability story; running OTel for traces / metrics while
keeping logs on Serilog only is a clean v1 split (operators who
need log export can add the Serilog OTel sink as a follow-up).

## Decision

### 1. Telemetry is opt-in

`[telemetry] enabled = false` (the default) means the composition
root skips OTel setup entirely. No exporter is built, no
`AddOpenTelemetry()` call runs, no listener attaches. The
`ActivitySource` / `Meter` instruments themselves are defined
unconditionally — but per the .NET runtime contract, an
`ActivitySource` with no listener performs essentially zero work,
so production CLI invocations that don't want OTel pay nothing.

### 2. Configuration: `[telemetry]` table

```toml
[telemetry]
enabled = true
otlp_endpoint = "http://otel-collector:4317"   # optional; falls
                                                # back to env var
service_name = "ivi-cli-lab"                    # required, default "ivi-cli"
traces_enabled = true
metrics_enabled = true
hislip_propagation = false   # §7: send W3C context to ivi-cli HiSLIP gateways
```

Validation surfaces three `TelemetryConfigError` variants:

- `TelemetryServiceNameEmpty` — service name was blank.
- `TelemetryEnabledButAllSignalsOff` — `enabled = true` but both
  `traces_enabled` and `metrics_enabled` are false.
- `TelemetryInvalidOtlpEndpoint` — endpoint URL did not parse.

When `otlp_endpoint` is null, the OTLP exporter honours the
SDK-standard `OTEL_EXPORTER_OTLP_ENDPOINT` env var. This matches
the convention every OTel-aware tool follows.

### 3. Instrumentation surface

Central registry: `IviCli.Application.Telemetry.IviCliTelemetry`.

**ActivitySources**:

- `IviCli.Backend` — one Activity per backend op
  (`backend.open` / `.close` / `.write` / `.query` / `.read`),
  tagged with `ivi.device`, `scpi.text`, `outcome`.
- `IviCli.Gateway` — gateway-side spans (see §6): one
  `gateway.session` per connection / link and one `gateway.message`
  per handled operation, on all three gateway servers.

**Meter `IviCli`**:

- `ivi.backend.op_duration` (Histogram&lt;double&gt;) — duration of an
  IIviBackend op. Tags: `op`, `ivi.device`, `outcome`.
- `ivi.pool.evictions` (Counter&lt;long&gt;) — pool entries closed by
  the idle / LRU sweep.
- `ivi.pool.lease_wait_timeouts` (Counter&lt;long&gt;) — lease attempts
  that exceeded `device.Timeout`.
- `ivi.pool.cached_entries` (ObservableGauge) — registered by the
  composition root once with a snapshot lambda, so multiple
  PoolingBackendFactory instances (tests) don't double-register.

**AspNetCore auto-instrumentation**: `AddAspNetCoreInstrumentation()`
attaches to the Management API listener so every
`/v1/devices/{name}/...` request emits a parent span that nests the
backend Activity.

### 4. Decorator stack with pool / capture

```
Capture(Pool(Instrumented(Default)))
```

`InstrumentingBackendFactory` always wraps the default factory,
regardless of `[telemetry] enabled`. Activity / Meter calls are
near-free without listeners, so the cost of always-on
instrumentation is trivial — and keeping it inside the pool /
capture layers means observers see the **wire truth**: one span
per real op, not one span per caller-initiated logical op.

Pool's elision still happens above the instrumented backend; the
pool's elided opens / closes do not emit `backend.open` spans
(correctly — there's no wire op to span). The pool's own
counters (`ivi.pool.*`) capture the deferred-close behaviour.

### 5. Composition root

`Program.cs` performs an **eager** `TomlConfigStore` load before
`BuildServiceProvider()` because OTel pipelines must be registered
on the `IServiceCollection` itself. The eager load uses the
production `FileSystem` directly — composition root is allowed to
know about Infrastructure (ADR 0010 §8).

`TelemetryBootstrapper.Install(services, config.Telemetry)`:

- No-op when `enabled = false`.
- Calls `services.AddOpenTelemetry().ConfigureResource(...)
  .WithTracing(...).WithMetrics(...)`.
- Tracing: adds the two ActivitySources + AspNetCore
  instrumentation + OTLP exporter.
- Metrics: adds the IviCli Meter + AspNetCore instrumentation +
  OTLP exporter.

### 6. Gateway spans (issue #17)

Every gateway server emits on `IviCli.Gateway`:

- **`gateway.session`** — one per accepted HiSLIP sync channel, per
  VXI-11 link, and per SOCKET connection carrying at least one SCPI
  line (a TCP probe that sends nothing emits no span). Tags:
  `ivi.transport` (`hislip` / `vxi11` / `socket`), `ivi.server`,
  `ivi.device`.
- **`gateway.message`** — one per handled operation (`ivi.operation`
  = `scpi` or `trigger`), child of its session span, tagged with the
  same identity tags plus `outcome` (`ok` / `error`). The backend's
  `backend.*` spans start while the message span is current, so a
  trace shows gateway → backend → device.

### 7. HiSLIP trace-context propagation (opt-in)

The binary gateway protocols carry no headers, so cross-process
traces need a carrier. HiSLIP has one designed for it: IVI-6.1
reserves message types 128–255 for vendors, and Table 16 requires a
peer that does not recognize a vendor message to answer with the
**non-fatal** Error code 3 ("Unrecognized Vendor Defined Message")
and return to normal processing — the session survives.

- `HiSlipMessageType.VendorTraceContext = 128`, sync channel,
  client → server, no response. Payload: UTF-8 W3C `traceparent`,
  optionally `'\n'` + `tracestate`.
- The **gateway always understands it**: the parsed context parents
  the next `gateway.message` span (consumed once), and the session
  span is attached as a link so the local view stays connected. An
  unparseable payload falls back to session parenting.
- The **client sends it only when `[telemetry] hislip_propagation =
  true`** (default `false`) and an Activity is current. The default
  stays off because a spec-conforming foreign server would answer
  with error chatter on every operation, and a non-conforming one
  might do worse; enable it when the HiSLIP peer is an ivi-cli
  gateway.

VXI-11 (fixed ONC RPC procedures) and raw SOCKET (SCPI bytes only)
have no extension point; their gateways emit local root traces.

### 8. Logs stay with Serilog

ADR 0011 §1 makes Serilog the canonical logger. v1 does **not**
emit log records via OTel. Operators who want log export can add
the Serilog OTel sink in a follow-up batch — it's an additive
change that doesn't touch the trace / metric pipelines this ADR
sets up.

### 9. Test strategy

- Unit tests subscribe directly to `ActivitySource` /
  `MeterListener` via the `System.Diagnostics` APIs. The OTel
  SDK itself is not exercised in unit tests; production wiring is
  a thin lambda the composition-root test covers indirectly.
- End-to-end with a real OTLP collector is operator-side work.

## Consequences

- **Operators get distributed tracing for free** when they enable
  the section — `GET /v1/devices/x/query` produces a span that
  nests the backend op span, with `scpi.text` and `outcome`
  attached for filtering.
- **Pool internals surface as metrics**, complementing the
  Batch M decision to surface broken sessions via `BackendError`.
  Dashboards see lease-wait-timeouts spike before users complain.
- **No-cost off-path**: with `enabled = false` (the default), the
  CLI process pays nothing — no exporters, no listeners, no
  Activity allocation overhead in any hot loop.
- **OTel package surface added**: three new packages
  (`OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `OpenTelemetry.Instrumentation.AspNetCore`) joined the central
  manifest. Versions pinned at the latest non-vulnerable set
  (1.15.3 / 1.13.0).

## Out of scope (v1)

- **Serilog → OTel logs sink.** v1 keeps logs on Serilog file +
  console sinks; OTel exporter is traces + metrics only.
- ~~**Gateway-server Activity emission.**~~ Shipped (§6–§7): all
  three gateways emit `gateway.session` / `gateway.message`, and
  HiSLIP can join the caller's trace through the opt-in
  VendorTraceContext message.
- **Custom samplers.** Default OTel sampler (parent-based) is
  fine for v1. Operators tune sampling via the OTel SDK env vars
  (`OTEL_TRACES_SAMPLER`).
- **HTTP outgoing instrumentation.** v1 doesn't add
  `AddHttpClientInstrumentation()` because the only outgoing
  HttpClient call in the codebase is the integration-test client
  itself. The package is available if a future batch needs it.
- **Metric aggregation customisation.** Default OTel histogram
  buckets are accepted; operators tune via the exporter config.
- **`gauge` for HiSlip / VXI-11 active sessions.** Surfaces in
  the gateway-server-touch batch alongside per-connection spans.
- **CLI flags for telemetry.** Config-file only for v1.
  `--otel-endpoint` etc. are a v2 add if operators ask.

## Verification

- `ivicli api start` (no telemetry config) → no exporter built, no
  OTel cost, no behavioural change.
- `[telemetry] enabled = true, otlp_endpoint =
  "http://localhost:4317"` → `ivicli api start` opens an OTLP gRPC
  connection at startup; `GET /v1/devices/x/status` produces a
  span tree.
- `[telemetry] enabled = true, traces_enabled = false,
  metrics_enabled = true` → only metrics flow.
- `dotnet test --filter "Category!=Integration"` proves the
  ActivitySource + Meter emit at the expected points with the
  expected tags.
- `GatewayActivityTests` runs each gateway against its real client
  over loopback and asserts the session / message span shapes, the
  propagated-parent case (message joins the caller's trace, session
  linked), and that the client sends no VendorTraceContext by
  default.
