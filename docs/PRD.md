**English** | [日本語](PRD.jp.md)

---

# PRD (Product Requirements Document)

## 1. Product Overview

`IVI-CLI` is an integrated CLI tool for managing, diagnosing, and operating instrument environments based on VISA/IVI.

Beyond SCPI operations, it unifies VISA/IVI environment diagnostics, alias management, remote instrument gateway, and CI/AI integration.

---

# 2. Goals

## 2.1 Primary Goals

### G1. Visibility into the VISA/IVI Environment

VISA implementation, devices, logical names, and backend state are all observable from the CLI.

---

### G2. Improved Instrument Operation UX

No more typing the VISA resource each time:

```bash
ivicli visa use psu1
ivicli visa query "*IDN?"
```

Provides a stateful UX of this form.

---

### G3. Remote Instrument Operation

Instruments attached to a remote PC can be operated transparently:

```bash
ivicli --server lab visa query psu1 "*IDN?"
```

---

### G4. Automation Friendly

Fits CI/CD, PowerShell, bash, Python, AI Agents, and Remote Labs.

---

# 3. Non-Goals

The following are out of scope for the initial release:

* GUI
* Waveform analysis
* Automatic driver generation
* Full IVI Configuration Store compatibility
* NI MAX-compatible UI
* Providing the VISA implementation itself
* Oscilloscope screen-capture analysis

---

# 4. Target Users

## Primary

* Instrument control / test automation engineers
* Embedded / FPGA developers
* SCPI/VISA users

## Secondary

* Remote Lab administrators
* CI / AI Agent developers

---

# 5. Core Concepts

## 5.1 VISA Resource Compatibility

IVI-CLI preserves compatibility with the existing VISA ecosystem.

Internally it operates on VISA resource strings, but in user-facing operations it prefers aliases / logical names.

---

## 5.2 Alias / Logical Name

Long VISA resources are mapped to short names:

```text
psu1
scope1
dmm1
```

---

## 5.3 Backend Abstraction

The CLI is backend-agnostic.

```text
LocalVisaBackend
HiSlipBackend
Vxi11Backend
SocketBackend
FakeBackend
ReplayBackend
```

are all usable transparently.

---

# 6. Command Structure

## 6.1 Command Namespace

```text
ivicli
 ├─ visa      # data plane / VISA transport / SCPI operation
 ├─ server    # gateway / remote instrument publishing
 ├─ logical   # logical name management
 ├─ config    # configuration management
 ├─ doctor    # environment diagnostics
 └─ driver    # IVI driver management
```

---

## 6.2 VISA Operations

### visa scan

```bash
ivicli visa scan                                   # LXI mDNS + VXI-11 broadcast
ivicli visa scan --port 5025 --port 1394           # also TCP-sweep the local subnet
ivicli visa scan --port 1394 --host 192.168.0.110  # probe a single known host
ivicli visa scan --port 5025 --subnet 10.0.0.0/24  # sweep an explicit subnet
ivicli visa scan --verbose                         # send *IDN? + show resolved Core port
```

Enumerates the VISA resources currently visible. Discovered resources are
grouped by host, so a device's VXI-11 / HiSLIP / SCPI-RAW access paths list
together — each host is probed on the well-known instrument ports (4880,
5025, and any `--port`) to surface every protocol it accepts, not just the
one that answered discovery.

`--port <n>` (repeatable) additionally TCP-sweeps the local subnet(s) to
find raw-SOCKET instruments that answer no broadcast or mDNS (e.g. a
Keithley on its vendor port). The sweep is opt-in and bounded to `/24`-or-
smaller subnets; `--subnet`/`--host` override the target set. `--verbose`
sends `*IDN?` to each open SOCKET endpoint and reports the model.

USB instruments are enumerated through the installed VISA runtime (the
VISA.NET shared components plus an implementation); on a machine without
one, USB entries are simply absent from the results.

Example output:

```text
[1] 192.168.0.10
      TCPIP0::192.168.0.10::inst0::INSTR
      TCPIP0::192.168.0.10::hislip0::INSTR
      TCPIP0::192.168.0.10::5025::SOCKET

[2] 192.168.0.110
      TCPIP0::192.168.0.110::1394::SOCKET
```

---

### visa add

```bash
ivicli visa add psu1 TCPIP0::192.168.0.10::inst0::INSTR
ivicli visa add 1 psu1
```

Registers an alias from a VISA resource or a scan index.

---

### visa list

```bash
ivicli visa list
```

Lists registered VISA targets.

---

### visa use

```bash
ivicli visa use psu1
ivicli visa use 1
ivicli visa use psu1 --default
```

Sets the current VISA target.

When `--default` is specified, the choice is persisted as the default target.

---

### visa current

```bash
ivicli visa current
```

Shows the currently selected target.

---

### visa query

```bash
ivicli visa query "*IDN?"
ivicli visa query psu1 "*IDN?"
```

Sends a SCPI query and prints the response.

---

### visa write

```bash
ivicli visa write "OUTP ON"
ivicli visa write psu1 "OUTP ON"
```

Sends a SCPI command.

---

### visa read

```bash
ivicli visa read
ivicli visa read psu1
```

Reads a response from the current VISA session.

---

### visa status

```bash
ivicli visa status
ivicli visa status psu1
```

Shows connection state, response time, and IDN of the VISA target.

---

### visa watch

```bash
ivicli visa watch                 # all registered devices, 1 s interval
ivicli visa watch psu1 dmm1 --interval 500
ivicli visa watch --plain --count 3
ivicli visa watch --json | jq
```

Live, periodically refreshed table of every (or a selected subset of) registered device's online state, latency, and last IDN response. Default render is a Spectre.Console live table; `--plain` emits ANSI-free per-tick snapshots for CI / log capture; `--json` emits one NDJSON object per tick. Ctrl+C exits cleanly.

---

### visa lint

```bash
ivicli visa lint smoke.scpi
ivicli visa lint smoke.scpi --json | jq
```

Static-analyses a `.scpi` script without running it. Flags unknown SCPI command roots against the IEEE 488.2 + SCPI Volume 1 vocabulary. v1 reports root-level mismatches only; full colon-path validation and parameter-syntax rules are deferred. Vendor-specific extensions are out of scope. Exit codes: file IO / parse failure → usage error, any `Error`-severity finding → generic failure, warnings only → 0.

---

## 6.3 Server Operations

The `server` namespace is the control plane for publishing and managing local VISA resources as a remote instrument gateway.

### server start

```bash
ivicli server start
ivicli server start --protocol hislip
```

Starts the gateway server.

---

### server stop

```bash
ivicli server stop
```

Stops the gateway server.

---

### server status

```bash
ivicli server status
```

Shows server state and published routes.

---

### server route list

```bash
ivicli server route list
```

```text
[hislip0] -> psu1
[hislip1] -> dmm1
```

---

### server route add

```bash
ivicli server route add hislip0 psu1
```

Binds a public instrument endpoint to a local device.

---

### server route remove

```bash
ivicli server route remove hislip0
```

Removes a route.

---

## 6.4 Diagnostics

### doctor

```bash
ivicli doctor
```

Diagnoses the following:

* IVI Shared Components
* VISA DLL
* VISA implementation
* PATH
* Backend selection

---

### visa traffic capture

Set `IVICLI_CAPTURE=<path>` (absolute, or relative to the rolling-log directory) and every backend operation across the CLI streams to a UTF-8 NDJSON file: one event per line carrying `timestamp` / `device` / `op` (`Open` / `Close` / `Write` / `Query` / `Read`) / `data` / `response` / `ok` / `latencyMs` / `error`. Activation is opt-in (no env var → null sink, zero overhead). Sink failures are swallowed so the operator's verbs never break because the audit sink does.

---

## 6.5 Driver / IVI Features

The `visa` namespace handles VISA transport / SCPI operation.

Vendor-specific IVI driver and logical-name operations are split into the `driver` and `logical` namespaces.

---

### driver list

```bash
ivicli driver list
```

Lists installed IVI drivers.

---

### logical list

```bash
ivicli logical list
```

Lists IVI logical names.

---

# 7. Remote Server

## 7.1 Remote Server Strategy

The Remote Server does not pick a proprietary RPC as the first choice; it prioritizes compatibility with industry-standard protocols that existing VISA clients can use.

Priority order:

```text
1. HiSLIP-compatible server
2. VXI-11-compatible server
3. Raw TCP Socket endpoint
4. IVI-CLI management API
```

`ivicli server` aims to be more than a proprietary gRPC gateway — it aims to be a remote instrument gateway that existing VISA clients can connect to as a TCPIP VISA resource.

A fifth server type, the USB/IP device server (§7.7), sits outside this ordering. The four above all serve a VISA client that opens a TCPIP resource; a USB/IP export serves the operating system's USB stack instead, so the instrument appears plugged in rather than reachable over the LAN.

---

## 7.2 HiSLIP-compatible Server

### Start

```bash
ivicli server start --protocol hislip
```

or:

```bash
ivicli server serve --protocol hislip
```

### Expected VISA resource

```text
TCPIP0::192.168.0.50::hislip0::INSTR
```

The HiSLIP endpoint is bound to the device registry inside IVI-CLI.

Example:

```toml
[[servers]]
name = "lab"
type = "hislip"
bind = "0.0.0.0"
port = 4880

[[routes]]
server = "lab"
endpoint = "hislip0"
device = "psu1"
```

---

## 7.3 VXI-11-compatible Server

Both halves of VXI-11 ship in Batch D — the `Vxi11GatewayServer` (server side) and the `Vxi11Backend` (client side). v1 implements create_link / device_write / device_read / device_clear / destroy_link plus a co-located portmapper GETPORT. Abort + interrupt channels, locking, trigger, and the real port-111 portmapper conversation remain deferred.

```bash
ivicli server start --protocol vxi11
```

---

## 7.4 Raw TCP Socket Endpoint

Provides SCPI over raw TCP socket.

```bash
ivicli server start --protocol socket --port 5025
```

Expected VISA resource:

```text
TCPIP0::192.168.0.50::5025::SOCKET
```

Raw socket is simple to implement, but advanced instrument control such as locking and SRQ is limited.

---

## 7.5 IVI-CLI Management API

HiSLIP / VXI-11 / Socket exist for compatibility with existing VISA clients.

The following management features, however, are exposed as a proprietary management API:

* device registry
* alias / logical name management
* route management
* status / monitor
* audit log
* authentication
* JSON output
* AI agent integration

**Shipped in Batch I — HTTP JSON**. ASP.NET Core minimal API embedded inside the CLI process; activate with `ivicli api start [--port 8080] [--bind 127.0.0.1]`. v1 endpoints: `GET /v1/{devices,servers,scenarios}` + `GET /v1/devices/{name}/status` + `POST /v1/devices/{name}/{query,write}` + `GET /openapi/v1.json` + `GET /healthz`. v1 binds to loopback by default; authentication, server-lifecycle endpoints, scenario import, and gRPC are v2.

**Batch J adds the WebSocket subprotocol**: `ws://host:port/v1/devices/{name}/visa` carries `{op,scpi}` frames and replies with `{event:response|ack|error,...}` for browsers / AI-agent runtimes / dashboards.

**Batch K adds PAT authentication**: mint tokens with `ivicli api token create`, validate via `Authorization: Bearer <token>` (HTTP) or `Sec-WebSocket-Protocol: ivi-cli-pat.<token>` (WS). Non-loopback bind requires ≥ 1 token (or `--allow-anonymous` to opt out). Token scopes, mTLS, expiry, and audit logging are v2.

---

## 7.6 Remote Access from IVI-CLI

IVI-CLI itself can speak both standard VISA resources and the management API.

```bash
ivicli --server lab visa query psu1 "*IDN?"
```

Internally, depending on configuration, it uses one of the following:

```text
Data Plane:
- local VISA
- HiSLIP VISA resource
- VXI-11 VISA resource
- raw TCP SOCKET resource

Control Plane:
- IVI-CLI management API
```

---

## 7.7 USB/IP Device Server

The LAN gateways stop at the client's socket. Code that reaches an instrument through a vendor VISA runtime's USB enumeration, or through a COM port, never touches them — and that code is exactly what a bench without hardware cannot test. A `usbip` server closes that gap: it exports a registered device over the USB/IP protocol, and the host's own USB/IP client attaches it as if it were plugged in. The operating system enumerates it, the class driver binds, and the VISA runtime lists it beside real instruments.

The client is the host's own tooling — usbip-win2 on Windows, the in-kernel `vhci-hcd` on Linux. IVI-CLI ships and installs no driver.

### Start

```bash
ivicli server add usb-srv --type usbip     # listens on 3240
ivicli server route add usb-srv 1-1 dut
ivicli server start usb-srv
```

A route's endpoint is a USB bus id rather than a LAN address, and each route is one emulated device.

### Attach from the host

```bash
usbip attach -r <host> -b 1-1
```

### Expected VISA resource

```text
USB0::0x1209::0x0001::dut::INSTR
```

The serial number is the device alias, which is how a host tells several exported instruments apart. A route's `profile` selects what the host enumerates:

| Profile | Enumerates as | Reached through |
| --- | --- | --- |
| `usbtmc` (default) | USBTMC-USB488 instrument, VID `0x1209` PID `0x0001` | the vendor VISA runtime's USB class driver; SCPI, status byte, and service requests |
| `cdc-acm` | USB serial device, PID `0x0002` | a COM port or `/dev/ttyACM*`, 115200 8-N-1, one SCPI line per newline |

```toml
[[servers]]
name = "usb-srv"
type = "usbip"
bind = "127.0.0.1"
port = 3240

[[routes]]
server = "usb-srv"
endpoint = "1-1"
device = "dut"
profile = "usbtmc"
```

One attach per device at a time; a second import while one is up is refused. USB/IP across a network is out of scope — the export is meant for the machine under test, and the default bind is loopback. See [ADR 0049](adr/0049-virtual-usb-mock-instrument.md).

---

# 8. Configuration

## 8.1 config.toml

The config file prefers an XDG-style path on all OSes.

```text
~/.config/ivicli/config.toml
```

The same default is used on Windows:

```powershell
$HOME/.config/ivicli/config.toml
```

Configuration paths are unified across platforms.

An environment-variable override is allowed when needed.

Example:

```text
IVICLI_CONFIG
```

---

## 8.2 Example

`config.toml` holds static configuration.

Dynamic session state such as the current device / current server is stored in a state directory.

`visa use` updates session state; `visa use --default` updates the defaults in `config.toml`.

Example:

```text
~/.local/state/ivicli/session.json
```

---

```toml
[defaults]
server = "local"
device = "psu1"

[[devices]]
name = "psu1"
resource = "TCPIP0::192.168.0.10::inst0::INSTR"
timeout_ms = 3000

[[devices]]
name = "scope1"
resource = "USB0::0x0699::...::INSTR"
timeout_ms = 5000

[[servers]]
name = "local"
type = "local"

[[servers]]
name = "lab"
type = "hislip"
host = "192.168.0.50"
port = 4880
```

`devices` / `servers` / `routes` do not embed dynamic names in table keys; they are defined as array of tables.

```toml
[[devices]]
name = "psu1"
resource = "TCPIP0::192.168.0.10::inst0::INSTR"
```

Adopting array of tables makes schemas, validation, and renames easier.

---

# 9. Output Modes

## Human-readable

```bash
ivicli visa status
```

## JSON

```bash
ivicli visa status --json
```

Intended for CI / AI Agents.

---

# 10. System Architecture

```text
                    Control Plane
┌─────────────────────────────────────────────────┐
│ config / logical / doctor / server route     │
└─────────────────────────────────────────────────┘
                        ↓
                Management API
                        ↓

                     Data Plane
┌─────────────────────────────────────────────────┐
│ visa query/write/read/status                   │
└─────────────────────────────────────────────────┘
                        ↓
                IIviBackend
                 ├─ LocalVisaBackend
                 ├─ HiSlipBackend
                 ├─ Vxi11Backend
                 ├─ SocketBackend
                 ├─ FakeBackend
                 └─ ReplayBackend
                        ↓
                    IVI/VISA
```

---

# 11. Naming Conventions

## 11.1 Product Naming

| Purpose      | Name    |
| ------------ | ------- |
| Display Name | IVI-CLI |
| Repository   | ivi-cli |
| Command      | ivicli  |
| Namespace    | IviCli  |

---

## 11.2 Command Naming

Command names follow these principles:

* lower-case
* avoid kebab-case
* verb-first
* prefer VISA ecosystem terminology

Examples:

```text
visa scan
visa query
server route add
logical list
```

---

## 11.3 Alias Naming

Device aliases / logical names should be short and readable.

Recommended:

```text
psu1
scope1
dmm1
awg1
```

Discouraged:

```text
rackA_main_scope_device
USB_SCOPE_01
```

---

## 11.4 Backend Naming

Backend implementations make transport / behavior explicit.

```text
LocalVisaBackend
HiSlipBackend
Vxi11Backend
SocketBackend
FakeBackend
ReplayBackend
```

---

# 12. Recommended Technology Stack

| Layer           | Technology                                                                        |
| --------------- | --------------------------------------------------------------------------------- |
| Language        | C#                                                                                |
| CLI             | System.CommandLine                                                                |
| Config          | Tomlyn                                                                            |
| Hosting         | Microsoft.Extensions.Hosting                                                      |
| Logging         | Microsoft.Extensions.Logging                                                      |
| Remote Protocol | HiSLIP / VXI-11 / TCP Socket                                                      |
| Management API  | gRPC / HTTP JSON                                                                  |
| VISA            | IVI Foundation / NI-VISA                                                          |
| Testing         | xUnit / NSubstitute / Shouldly                                                    |
| Test Helpers    | Microsoft.Extensions.Logging.Abstractions / System.IO.Abstractions.TestingHelpers |

---

# 13. Testing Strategy

## 13.1 Test Stack

Unit tests are built on:

| Purpose                 | Package                                   |
| ----------------------- | ----------------------------------------- |
| Test Framework          | xUnit                                     |
| Mock / Substitute       | NSubstitute                               |
| Assertion               | Shouldly                                  |
| Logging Abstraction     | Microsoft.Extensions.Logging.Abstractions |
| File System Test Double | System.IO.Abstractions.TestingHelpers     |

---

## 13.2 Test Targets

Phase 1 focuses on:

* command parsing
* config load / save / validation
* session state load / save
* alias / resource resolution
* backend abstraction
* connection lifecycle
* timeout / disconnect / reconnect behavior
* JSON output contract
* error handling

Real-hardware-dependent parts prefer `FakeBackend`; tests against real instruments are isolated as integration tests.

---

## 13.3 Connection Lifecycle Testing

Connect / disconnect / timeout must be fault-injectable via `FakeBackend` / `FakeSession`.

Example test cases:

* open success / open failure
* query timeout
* read timeout
* disconnected during query
* reconnect after failure
* dispose / close called exactly once
* online / offline judgment in `visa status`

Tests against real hardware are placed in the `integration` category and isolated from regular unit tests.

---

# 14. MVP Scope

## In MVP

Phase 1:

* visa scan
* visa list
* visa add/remove
* visa use/current
* visa query/write/read
* visa status
* doctor
* driver list — enumerate installed IVI drivers from the local IVI
  Configuration Store. Essential for debugging when an
  instrument is online but the driver assembly is missing or mis-
  versioned.
* logical list — enumerate IVI logical names from the same store.
  Operators use this to confirm which alias maps to which driver
  session before reaching for `visa add`.
* config.toml
* --json

Phase 2:

* server start/stop/status
* server route add/remove/list
* HiSLIP-compatible server
* remote instrument gateway

Phase 3 (operator-facing automation):

* visa script — execute a sequence of SCPI commands from a file with
  per-line dispatch (write / query / sleep / assert) against the active device.
* visa monitor — poll a query at a fixed interval and stream timestamped
  responses to stdout (and optional structured log file), until interrupted.
* mock scenario record — append observed query/write traffic into a
  scenario file for later playback.
* mock scenario import — convert an NDJSON capture (IVICLI_CAPTURE
  output) into a stored MockScenario so the existing IVICLI_REPLAY
  machinery can serve it.
* mock received — read back the SCPI writes a device received from an
  IVICLI_CAPTURE traffic log, so a test that drives the mock through its
  own VISA stack can confirm out-of-band which writes actually arrived.
* server log — tail the gateway's per-server structured log file with
  optional follow / level filter (operator-facing observability).

---

## Not in MVP

* GUI
* Web UI
* Authentication
* TLS
* Streaming waveform
* Full VXI-11 compatibility
* VISA packet replay
* AI integration

---

# 15. Future Extensions

## Planned

* Web UI
* AI agent integration

---

# 16. UX Philosophy

`IVI-CLI` aims to be an instrument-infrastructure operations CLI, not a SCPI shell.
