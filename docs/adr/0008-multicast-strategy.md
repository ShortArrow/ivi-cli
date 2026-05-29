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
| **Tertiary (deferred)** | **`nmap`-style subnet TCP sweep** | Last resort for instruments without mDNS or VXI-11 announcements | Security-team-hostile (IDS/IPS hits); slow; high false-positive rate |
| **OS-native** | **NI-VISA `viFindRsrc`** (via Local backend on Windows) | Re-uses an installed VISA runtime that already does mDNS + portmapper + USB-TMC | Windows + vendor SDK required; not a 1st-class ivi-cli path |

Batch W ships Primary + Secondary. Tertiary is deferred — operators
who need it can build the same sweep on top of `nmap -sT -p 5025,4880`
and pipe the result into `ivicli visa add`. OS-native is implicit
through the existing `LocalBackend` Local-VISA path; the discovery
flow lives there for users who already have NI-VISA installed.

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

A minimal ONC RPC encoder (no dependency on `Vxi11Backend`) sends
`PMAPPROC_GETPORT` for the VXI-11 Device Core program
(`0x0607AF`, version 1) over UDP broadcast `255.255.255.255:111`.
Any host that registers the program in its portmapper replies with
the TCP port; the scanner records the sender IP and builds
`TCPIP0::<sender>::inst0::INSTR`.

Wire format follows RFC 1833 exactly:

- AUTH_NONE credentials + verifier (flavor = 0, length = 0)
- Big-endian XDR throughout
- 72-byte request, 28-byte minimum successful reply

The scanner does not chase the per-host TCP port (e.g. by issuing
`create_link`) — the portmapper response is sufficient evidence
that the standard `Vxi11Backend.OpenAsync` path can reach the
instrument.

### 4. `visa scan` UX

```sh
ivicli visa scan                                # human-readable list
ivicli visa scan --json                         # machine-readable
ivicli visa scan --add                          # also register every result
ivicli visa scan --add --add-timeout-ms 5000    # override default 3000 ms
```

Auto-registration uses a deterministic alias derived from the
resource shape so repeated invocations are idempotent:

| Variant | Alias |
| --- | --- |
| TCPIP | Sanitized host portion (lowercase, non-alnum → `-`) |
| USB | `usb-<serial>` |
| GPIB | `gpib-<primary-address>` |

Existing alias collisions are surfaced as "skipped (alias taken)"
rather than errors.

### 5. Cancellation + timeout semantics

- The discovery window per scanner is fixed at 3 s by default; future
  work may surface this as a CLI flag once operators ask.
- A user `Ctrl+C` cancels mid-scan; each scanner respects the
  `CancellationToken` and returns what it has collected so far rather
  than throwing (graceful degradation).
- Aggregation surfaces partial results: even if the VXI-11 broadcast
  fails (e.g. UDP/111 blocked on the host), mDNS results still come
  through (`ScanDevicesQueryHandler` swallows per-scanner failures
  unless every scanner failed).

### 6. Mock-container compatibility

The discovery scanners are registered unconditionally in the CLI
composition root — even inside the mock-VISA container (ADR 0018).
When the container runs alone on its Docker network, both probes
silently return zero responders. No special-case code path; the
behaviour is exactly what the user would see on an isolated VLAN.

### 7. Out of scope (v1)

- **`nmap`-style subnet sweep.** Adds security-noise and false
  positives. A future batch may add `visa scan --subnet
  <cidr>` with an explicit warning.
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
  helpers (alias derivation, resource roundtrip) — 9 tests in
  `IviCli.Cli.Tests/Commands/VisaScanCommandTests.cs`.
- Manual: on a LAN with an LXI-conformant instrument, run
  `ivicli visa scan` and confirm the instrument appears within
  the 3-s window. With `--add`, confirm `visa list` shows the new
  alias; a follow-up `visa query <alias> "*IDN?"` returns the
  expected response.
- Negative: on an isolated host (Docker container with no other
  hosts on its bridge), `ivicli visa scan` returns
  `(no resources discovered)` without exception or timeout.
