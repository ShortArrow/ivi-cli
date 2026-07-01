# 0008. Discovery & Multicast Strategy

- Status: Accepted
- Date: 2026-05-29

## Context

`ivicli visa scan` is the discovery surface declared in PRD §6.2:
"enumerate VISA resources visible to the registered backends." The
port (`IBackendScanner`) + the aggregating handler
(`ScanDevicesQueryHandler`) shipped early in Phase 1, but until
Batch W only the `FakeBackendScanner` test double was registered
— `visa scan` ran cleanly but returned nothing on real LANs.

Discovery is the standard "first-mile" workflow for VISA users:

1. The operator opens an instrument's web page or just powers it on.
2. The user runs a discovery tool to find its IP and resource string.
3. The user registers the resource (`visa add`).
4. Subsequent commands use the alias.

Without (2), every alias requires a manual lookup — friction that
contradicts ivi-cli's stateful-UX positioning ("register once,
operate by name"). This ADR commits the project to a tiered
discovery strategy backed by industry-standard mechanisms.

## Decision

### 1. Discovery sources (tiered)

| Tier | Mechanism | Pros | Cons |
| --- | --- | --- | --- |
| **Primary** | **LXI mDNS / DNS-SD** | LXI 1.4+ standard; instruments self-announce; vendor + model + serial available in TXT records; subnet-local with zero config | Skips legacy instruments without mDNS; requires same broadcast domain |
| **Secondary** | **VXI-11 portmapper broadcast (UDP 111)** | Catches pre-mDNS VXI-11 devices; aligned with RFC 1833 portmapper; ivi-cli already has XDR encoders | Slower per-host turnaround; some networks filter UDP/111 |
| **Tertiary (opt-in)** | **Subnet TCP sweep (`--port`)** | Last resort for raw-SOCKET instruments without mDNS or VXI-11 announcements (e.g. Keithley 2701 on vendor port 1394) | Active probing (IDS/IPS-visible); slower; opt-in only | 
| **OS-native** | **NI-VISA `viFindRsrc`** (via Local backend on Windows) | Re-uses an installed VISA runtime that already does mDNS + portmapper + USB-TMC | Windows + vendor SDK required; not a 1st-class ivi-cli path |

Primary + Secondary run unconditionally. Tertiary is **opt-in** via
`visa scan --port <n>` (§4) — off by default so a bare `visa scan`
never generates active TCP probes. OS-native is implicit through the
existing `LocalBackend` Local-VISA path; the discovery flow lives there
for users who already have NI-VISA installed.

### 2. LXI mDNS service types

The scanner queries all four LXI 1.4+ service types in parallel:

| Service type | Maps to |
| --- | --- |
| `_hislip._tcp.local` | `TCPIP0::<host>::hislip0::INSTR` |
| `_vxi-11._tcp.local` | `TCPIP0::<host>::inst0::INSTR` |
| `_scpi-raw._tcp.local` | `TCPIP0::<host>::<port>::SOCKET` |
| `_lxi._tcp.local` | (generic LXI marker — surfaced via the protocol-specific announcements when the same device also publishes them; skipped on its own) |

The library is **Makaretu.Dns** + **Makaretu.Dns.Multicast** (MIT
license, pure managed). Discovery window: 3 s default. Each
`ServiceInstanceDiscovered` event's Additional records carry the
SRV + A / AAAA records needed to build the host:port tuple in a
single round-trip.

### 3. VXI-11 portmapper broadcast

The scanner sends `PMAPPROC_GETPORT` for the VXI-11 Device Core
program (`0x0607AF` / 395183, version 1) over UDP/111. Any host that
registers the program in its portmapper replies with the TCP port;
the scanner records the sender IP and builds
`TCPIP0::<sender>::inst0::INSTR`. The GETPORT request/reply codec is
shared with the client backend's unicast portmapper resolution via
`Vxi11Portmapper` (ADR 0029).

**Per-interface directed broadcast.** The scanner enumerates every
operational, non-loopback IPv4 interface and sends one probe per NIC,
bound to that NIC's local address, addressed to the interface's
**subnet-directed** broadcast (e.g. `192.168.3.255:111`). A limited
broadcast (`255.255.255.255`) egresses only a single interface on a
multi-homed host — typically whichever owns the default route — so
on a machine with the instruments on a secondary lab NIC it never
reaches them. Probing each subnet directly fixes that. Replies are
de-duplicated by sender IP across all interfaces.

Wire format follows RFC 1833: AUTH_NONE credentials + verifier
(flavor = 0, length = 0), big-endian XDR, 28-byte minimum successful
reply.

The scanner does not chase the per-host TCP port (e.g. by issuing
`create_link`) — the portmapper response is sufficient evidence
that the standard `Vxi11Backend.OpenAsync` path can reach the
instrument.

**Inherent limits.** Broadcast/multicast discovery is link-local: it
cannot cross a router into another subnet (limited broadcast is never
forwarded; directed broadcast is dropped by routers by default per
RFC 2644; mDNS is TTL-scoped), and it only finds instruments that
answer a broadcast GETPORT or advertise mDNS. Instruments on another
subnet are reached by `visa add` with the known address, not by scan.

### 4. Active socket sweep and host enrichment

Broadcast/mDNS discovery has two blind spots that this section closes:

1. **Raw-SOCKET-only instruments** (no VXI-11, no mDNS) — e.g. a
   Keithley 2701, which speaks SCPI only on its vendor port 1394.
2. **Discovered devices' other access paths** — a device found via
   VXI-11 broadcast (`inst0::INSTR`) frequently also speaks HiSLIP and
   SCPI-RAW, but broadcast only surfaces the one protocol that answered.

**Socket sweep (`SocketSweepScanner`, opt-in via `--port`).** For each
`--port <n>` (repeatable), the scanner opens a bounded-timeout TCP
connection to every target address and reports
`TCPIP0::<host>::<n>::SOCKET` for each host that accepts. Targets default
to every operational IPv4 subnet no larger than a `/24`; APIPA
(`169.254/16`) and oversized subnets are skipped so a stray `/16` never
becomes a 65k-probe scan. `--subnet <cidr>` and `--host <ip>` override
the target set. Concurrency is bounded (128 in flight). The CIDR/subnet
math and interface-selection predicate are pure functions
(`SocketSweepTargets`) covered by unit tests; only the socket round-trip
is environment-dependent (behind `IEndpointProber`).

**Host enrichment (`ScanDevicesQueryHandler`).** After discovery, every
host that any scanner surfaced is probed on the well-known instrument
ports it has not already reported — HiSLIP `4880` →
`hislip0::INSTR`, SCPI-RAW `5025` → `5025::SOCKET`, plus any `--port`
values — and a resource is appended per reachable protocol (deduped by
canonical resource string). This is why a Kikusui PWR-series device found
via VXI-11 now also lists its HiSLIP and SCPI-RAW access paths. An open
`4880` is taken as evidence of HiSLIP and is never sent a raw `*IDN?`
(that needs the HiSLIP handshake).

**Identification (`--verbose`).** By default the sweep and enrichment
only report which ports are open — fast, no payload. `--verbose` sends
`*IDN?` to each open SOCKET endpoint and attaches the model, and surfaces
diagnostics such as the VXI-11 Core port the portmapper resolved. The
Core port stays a diagnostic, never the canonical resource: it is a
dynamic port that changes across instrument reboots, so the registered
resource stays the port-less `inst0::INSTR` and the client re-resolves it
via the portmapper on every connect (ADR 0029).

### 5. `visa scan` UX

```sh
ivicli visa scan                                # human-readable list (broadcast + mDNS)
ivicli visa scan --json                         # machine-readable
ivicli visa scan --add                          # also register every result
ivicli visa scan --add --add-timeout-ms 5000    # override default 3000 ms
ivicli visa scan --port 5025 --port 1394        # also TCP-sweep the local subnet
ivicli visa scan --port 1394 --host 192.168.3.110  # probe a single known host
ivicli visa scan --port 5025 --subnet 10.0.0.0/24  # sweep an explicit subnet
ivicli visa scan --verbose                      # send *IDN? + show resolved Core port
```

Human output groups resources by host so a device's VXI-11 / HiSLIP /
SCPI-RAW access paths list together.

Auto-registration uses a deterministic alias derived from the
resource shape so repeated invocations are idempotent:

| Variant | Alias |
| --- | --- |
| TCPIP | Sanitized host portion (lowercase, non-alnum → `-`) |
| USB | `usb-<serial>` |
| GPIB | `gpib-<primary-address>` |

Existing alias collisions are surfaced as "skipped (alias taken)"
rather than errors.

`visa scan` prints the **unmasked** resource string (real host) in
both human and `--json` output — it is user-requested discovery
output, not a log line, so the `ToLogString()` masking rule (ADR 0017,
scoped to logging) does not apply. This matches the value `--add`
writes to config.

### 6. Cancellation + timeout semantics

- The discovery window per scanner is fixed at 3 s by default; future
  work may surface this as a CLI flag once operators ask.
- A user `Ctrl+C` cancels mid-scan; each scanner respects the
  `CancellationToken` and returns what it has collected so far rather
  than throwing (graceful degradation).
- Aggregation surfaces partial results: even if the VXI-11 broadcast
  fails (e.g. UDP/111 blocked on the host), mDNS results still come
  through (`ScanDevicesQueryHandler` swallows per-scanner failures
  unless every scanner failed).

### 7. Mock-container compatibility

The discovery scanners are registered unconditionally in the CLI
composition root — even inside the mock-VISA container (ADR 0018).
When the container runs alone on its Docker network, both probes
silently return zero responders. No special-case code path; the
behaviour is exactly what the user would see on an isolated VLAN.

### 8. Out of scope

- **Cross-subnet / routed discovery.** Broadcast and mDNS stay
  link-local; the `--port` sweep only walks subnets the host is a
  member of. Reaching an instrument on another subnet is a `visa add`
  with the known address, or an explicit `--subnet <cidr>` sweep of a
  reachable range.
- **Configurable discovery window** (`--timeout-ms`). Defaults
  are fine for v1; flag added when an operator asks.
- **Per-source toggles** (`--mdns` / `--vxi-11`). Single-source
  invocations are an obvious extension but rarely useful — the
  cost of running both is ~3 s of wall time once.
- **TXT-record-driven IDN annotation.** The LXI spec defines
  `mfg`, `model`, `sn` in the `_lxi._tcp` TXT record; the v1
  scanner ignores them and reports `Idn = null`. Future work
  may attach the TXT-derived `*IDN?`-style string when
  available.
- **Continuous discovery** (subscribing to mDNS announcements
  long-term inside `api start` or `server start`). A future batch
  may add a `discover watch` mode.
- **IPv6 reverse lookup.** The mDNS scanner prefers IPv4 (single
  address slot); IPv6 is captured only when no A record is
  present.

## Consequences

- **`ivicli visa scan` becomes useful on day one** for LXI-conformant
  instruments — no operator-side wiring, no `visa add` with a hand-
  copied IP.
- **`--add` closes the discover → register loop**: one command can
  populate a complete device list for a freshly-powered-on rack.
- **Discovery surfaces non-running gateways too** — the VXI-11
  portmapper broadcast catches anything with a registered VXI-11
  program even if it's currently not accepting TCP connections.
  Operators should still confirm with `visa query <alias> "*IDN?"`
  before depending on the alias.
- **No new background services**: scanners run only on `visa scan`
  invocation. `api start` / `server start` don't carry a discovery
  cost.

## Related work

- **lxi-tools** (https://github.com/lxi-tools/lxi-tools) — established
  C-language CLI with `lxi discover`, `lxi scpi`, `lxi screenshot`,
  `lxi benchmark`. Subset overlap on the basic SCPI + discovery
  surface; lxi-tools wins on screenshot + benchmark + Linux
  packaging + the `liblxi` library form. ivi-cli's gravity center is
  different — stateful UX, scenario mock, mock container (ADR 0018),
  gateway-server mode, capture/replay, Management HTTP/WebSocket API,
  PAT auth, audit log. The discover capability is parallel: both
  projects implement the same RFC mechanisms because there's only
  one wire-protocol answer.
- **NI-VISA `viFindRsrc`** — vendor SDK; transparently does mDNS +
  portmapper + USB-TMC under the hood. ivi-cli's `LocalBackend` ends
  up using this on Windows; the explicit ADR 0008 scanners are an
  OS-portable alternative that doesn't require a vendor SDK install.
- **PyVISA-py + zeroconf** — Python ecosystem reference for mDNS-only
  discovery; informed the service-type list above.
- **Keysight IO Libraries, R&S RS-Visa** — proprietary equivalents
  to NI-VISA with comparable discovery semantics.

## Verification

- `dotnet test --filter "Category!=Integration"` covers the pure
  helpers: alias derivation / resource roundtrip
  (`IviCli.Cli.Tests/Commands/VisaScanCommandTests.cs`), the sweep
  CIDR / subnet math and interface predicate
  (`IviCli.Backends.Socket.Tests/SocketSweepTargetsTests.cs`), the
  sweep scanner driven by a fake prober
  (`SocketSweepScannerTests.cs`), and the enrichment pipeline
  (`IviCli.Application.Tests/.../ScanDevicesQueryHandlerTests.cs`).
- Manual: on a LAN with an LXI-conformant instrument, run
  `ivicli visa scan` and confirm the instrument appears within
  the 3-s window. With `--add`, confirm `visa list` shows the new
  alias; a follow-up `visa query <alias> "*IDN?"` returns the
  expected response.
- Negative: on an isolated host (Docker container with no other
  hosts on its bridge), `ivicli visa scan` returns
  `(no resources discovered)` without exception or timeout.
