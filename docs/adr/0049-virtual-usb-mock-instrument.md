# 0049. Virtual USB Mock Instrument

- Status: Accepted
- Date: 2026-08-07

## Context

The mock instrument stack has one blind spot. Four tiers exist, and the
distinction between them matters enough to fix the vocabulary here:

| Tier | USB device | Instrument content | What it exercises |
| --- | --- | --- | --- |
| A | none (in-process `FakeBackend`) | mock | application layer and gateways; VISA, kernel drivers, and USB transport are bypassed |
| B | **software-emulated** ("a virtual device behaving as a virtual instrument") | mock | the whole host stack — Windows USB core, the inbox USBTMC class driver, any vendor VISA, ivi-cli's own backends |
| C | **physical gadget silicon** ("a real device behaving as a virtual instrument") | mock | everything, cable and enumeration timing included |
| D | physical instrument | real | everything, plus uncontrollable firmware quirks |

Vendor-VISA visibility alone does not require USB: the existing mock
gateways ([ADR 0018](0018-mock-container.md)) already appear to any VISA
as `TCPIP::…::INSTR` resources. What no current tier below D can exercise
is the **USB host-stack path** — and that is exactly where the 2026-08
bench failures lived: kernel driver binding, per-mechanism event support
(the CLR handler mechanism NI-VISA rejects on USB, [ADR 0041 §5](0041-trigger-and-srq-ports.md)),
and an instrument whose SRQ notification machinery wedges until a power
cycle. None of that is reproducible without a USB device the host can
enumerate; all of it should be scriptable instead of rediscovered on a
bench.

Tier C needs gadget-capable hardware. Tier B does not, and is chosen
first.

## Decision

### 1. A managed USB/IP device server

ivi-cli gains a USB/IP **device server**: a pure C# implementation of the
USB/IP wire protocol (`OP_REQ_DEVLIST` / `OP_REQ_IMPORT`,
`USBIP_CMD_SUBMIT` / `USBIP_CMD_UNLINK` over TCP) that exports one
emulated USB device per bound mock device. No native code and no kernel
component on the server side — the same "speak the protocol ourselves"
property the HiSLIP / VXI-11 / SOCKET backends already have.

An instrument carries one attach at a time, the way a physical one does
— not one attach per exported busid. `OP_REQ_IMPORT` for a device
already attached, through the busid asked for or through any other route
that exports it, is answered with the error status, the same reply an
unknown busid gets: the client reports a failed attach and commits no
port. Detaching frees the instrument for the next import.
`OP_REQ_DEVLIST` still lists it, because the reply has nowhere to say
otherwise and a client learns a device is taken by being refused it.

Per-instrument rather than per-busid is what makes two routes onto one
device a usable shape instead of a misconfiguration: export the same
mock as USBTMC and as CDC-ACM, and the operator picks which one the host
sees by attaching that busid, with no configuration edit between. The
two simply take turns.

### 2. The exported device is USBTMC-USB488

The emulated device presents interface class `0xFE`, subclass `0x03`,
protocol `1`, with bulk-OUT, bulk-IN, and interrupt-IN endpoints, and
implements:

- USBTMC bulk framing: `DEV_DEP_MSG_OUT`, `REQUEST_DEV_DEP_MSG_IN`,
  `DEV_DEP_MSG_IN`, including transfer-size splitting and the EOM flag;
- the USB488 control requests: `READ_STATUS_BYTE`, `GET_CAPABILITIES`
  (declaring SR1), `INITIATE_CLEAR` / `CHECK_CLEAR_STATUS`;
- SRQ notifications on the interrupt-IN endpoint, so a host-side
  subscriber sees real service request events.

### 3. SCPI semantics come from the existing mock engine

The device's behavior is the scenario stack that already drives the LAN
gateways: scenes answer queries, rules declare the status byte they
raise a service request with, and quirk profiles — including the
notify-wedge shape observed on the bench — apply unchanged. One scenario
TOML therefore describes the same mock instrument whether it is reached
over HiSLIP, VXI-11, raw socket, or USB.

### 4. Client attach is the operator's, documented, never automated

- Windows: [usbip-win2](https://github.com/vadimgrn/usbip-win2), pinned
  to a release whose drivers are WHLK-certified — signed by Microsoft
  through the Windows Hardware Compatibility Program — so a stock
  Windows 11 host attaches without test mode. Signing varies from
  release to release of that project, so the pin is to a certified
  release, not to the project. It is a user-installed tool; ivi-cli
  never bundles or installs drivers (the stance ADR 0048 §2 fixed for
  the host side holds for the mock side too).
- Linux: the in-kernel `vhci-hcd` client that ships with the kernel's
  own USB/IP support.

Once attached, the device enumerates like any USBTMC instrument: the
inbox class driver binds, every vendor VISA lists it as
`USB0::<vid>::<pid>::<serial>::INSTR`, and ivi-cli's own USB scanner
finds it like any other instrument.

Neither client is a dependency of this design. The dependency is the
USB/IP wire protocol itself — usbip-win2 and the kernel's `vhci-hcd` are
interchangeable peers speaking it, and any conforming client attaches the
same exported device. ADR 0048 §1 draws that line for VISA, where the IVI
Foundation abstraction may be depended on and a vendor implementation may
not; the same line runs through the transport here.

### 5. Debuggable by generic tools, not only through VISA

A mock is only as debuggable as the tools that can watch it. Three
observation layers, each served by tooling that already exists:

- **The VISA API layer stays traceable.** Because the device enumerates
  through every vendor VISA as an ordinary resource, the vendors' own
  call monitors — NI I/O Trace, Keysight IO Monitor, TekVISA's
  OpenChoice Call Monitor — record each `viOpen` / `viWrite` /
  `viReadSTB` against the mock exactly as against real hardware.
  Vendor-VISA enumeration is therefore a debugging channel in its own
  right, not merely a compatibility checkbox.
- **Wire captures need no USB capture driver.** Every URB travels as
  USB/IP over TCP, and Wireshark dissects that natively (the built-in
  `usbip` dissector) — a loopback capture shows every transfer,
  descriptor, and interrupt-IN notification. USBPcap, which inserts a
  filter driver on physical host controllers and whose behavior on an
  emulated controller is unverified, is never required.
- **A serial-shaped profile for serial-shaped tools.** Besides the
  USBTMC-USB488 profile, the server can export a **CDC-ACM** device;
  the inbox `usbser.sys` binds and a real COM port appears, so serial
  terminals (TeraTerm and kin) talk SCPI to the same scenario engine —
  and a vendor VISA sees an `ASRL` resource. The profile is selected
  per exported device — `profile = "cdc-acm"` on the route, or
  `server route add <server> <busid> <device> --profile cdc-acm`;
  USBTMC remains the default and a route that says nothing keeps it.
  (Raw-TCP terminals already reach the mock today through the SOCKET
  gateway; the CDC-ACM profile exists for tools that only speak COM.)

  What travels over a CDC export is a byte stream, so the framing is
  the SOCKET gateway's: a line ends at a newline, a blank line is
  nothing, and a trailing `?` makes a query. What does not travel is
  the service request — a COM port has no channel for one, the profile
  claims no `SERIAL_STATE` notification, and the notification endpoint
  CDC 1.1 §3.3.1 obliges it to declare stays idle for the life of the
  attach. An SRQ-shaped test therefore belongs to the USBTMC profile,
  which is the other reason USBTMC is the default.

### 6. Out of scope

- Tier C (gadget hardware) — stays open as a possible future step; this
  ADR neither commits to nor forecloses it.
- Isochronous transfers, USB 3 features, device firmware update
  surfaces, and interfaces beyond what the selected profile requires
  (USBTMC uses one; CDC-ACM uses its standard control + data pair).
- Automated driver installation of any kind.

## Consequences

**Pros**

- The USB host-stack failure classes seen on the bench become scripted,
  repeatable tests — including SRQ delivery and the wedge quirk.
- One scenario document drives the mock across every transport ivi-cli
  speaks, USB included.
- The stage-2 native USBTMC backend (ADR 0048) gains a loopback partner:
  the host stack under development and this device server verify each
  other on one machine, no vendor instrument required.
- Debugging stays in ordinary tools: Wireshark decodes the whole USB
  conversation from a loopback capture, and the CDC-ACM profile puts
  the mock behind a plain COM port for serial terminals.

**Cons**

- The USBTMC/USB488-over-URB surface is a substantial protocol
  implementation with its own state machines.
- Windows attach depends on a third-party client (usbip-win2); CI cannot
  exercise the attach step on hosted Windows runners, so end-to-end runs
  stay bench-side (Linux `vhci-hcd` in CI is a possible later step).

## Verification

The claim under test: a scenario-backed device exported by a `usbip`
server is, to the host's USB stack and to the VISA runtime above it, a
USBTMC-USB488 instrument (or, with the CDC-ACM profile, a serial port)
that enumerates, answers SCPI, reports its status byte, and raises
service requests — indistinguishable from hardware for the code under
test.

**Context.** Windows 11 x64 with usbip-win2 (WHLK-certified release)
and NI-VISA, which supplies the USBTMC class driver (Windows ships
none); Linux through the kernel's `vhci-hcd` client. **Assumptions.**
One attach per instrument at a time; device names are distinct, since
the host tells devices apart by VID/PID/serial and the serial is the
device name; the mock has no status-register model, so a scenario rule
declares the status byte it raises a service request with. **Out of
scope.** Defects of the host's USB/IP client; VISA runtimes other than
the one named above; several `server start` processes exporting the
same device name (each process is its own mock, so their scene state
and service requests are not shared); more than a few devices per
server; USB/IP across a network.

**What the suite holds.** Every claim a test can express lives in the
test suite and nowhere else: descriptor tables and the golden
configuration blobs, USBTMC framing and the bTag discipline, the USB488
control requests and the interrupt-IN notification format, CDC-ACM line
coding and the DTR session boundary, devlist/import/unlink over a
kernel-free USB/IP client, per-instrument attach exclusivity, several
devices served side by side, and a scenario rule's service request
reaching a HiSLIP client and a parked interrupt URB. No kernel attach
runs in CI.

**What only a host can show.** Two attach routes cover the rest, and only
one needs anything installed. The zero-dependency route runs against
the kernel's own client: WSL2 carries `vhci-hcd`, and under mirrored
networking the Windows listener is reachable at `localhost`, so `usbip
list` and `usbip attach` exercise devlist, import, and full enumeration
against the reference implementation. For the USBTMC profile that route
ends at enumeration — the Microsoft WSL kernel ships no `usbtmc`
module — while the CDC-ACM profile goes all the way, because `cdc_acm`
is in that kernel and a `/dev/ttyACM*` appears.

The Windows-side attach is the release-gating check, run on a real host
before a release per the ADR 0047 policy: attach through usbip-win2;
confirm the device binds to the USBTMC class driver the vendor VISA
installs; confirm the vendor VISA and `ivicli visa scan` both enumerate
it; run a query round-trip against a scenario; run the IEEE 488.2 SRQ
sequence (`*ESE 1; *SRE 32; *OPC` against a rule carrying `srq`) and see
the service request arrive as a VISA event; attach the CDC-ACM profile
and talk SCPI through the COM port; attach several devices at once and
see their answers and service requests stay apart; and read one attach
cycle back through Wireshark's `usbip` dissector without a malformed
frame. Each run is recorded, with its date and method, on the issue that
tracks this feature.

**Residual risk — what this case does not assure.** The VISA-API-layer
observation (§5's first layer) is reasoned, not observed: the vendors'
call monitors are GUI tools, and their view of NI-VISA calls is
device-agnostic. Only one host configuration has been observed; other
VISA runtimes and Linux hosts beyond WSL2 have not. On a Linux kernel
that carries `usbtmc`, the class driver's binding to the export is
expected but not observed. Under the notify-wedge quirk the mock's
`READ_STATUS_BYTE` reports the last byte the notification path saw,
whereas the instrument that motivated the quirk kept reporting MSS in
`*STB?`; a scenario wanting that answer writes the `*STB?` rule. The
VID/PID pair is pid.codes' test allocation, chosen for a mock and frozen
by the first release that ships it, since resource strings and driver
bindings key on it.
