# 0049. Virtual USB Mock Instrument

- Status: Proposed
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
gateways: scenes answer queries, the status model raises SRQs, and quirk
profiles — including the notify-wedge shape observed on the bench —
apply unchanged. One scenario TOML therefore describes the same mock
instrument whether it is reached over HiSLIP, VXI-11, raw socket, or
USB.

### 4. Client attach is the operator's, documented, never automated

- Windows: [usbip-win2](https://github.com/vadimgrn/usbip-win2) — its
  drivers carry a Microsoft attestation signature, so a stock Windows 11
  host attaches without test mode. It is a user-installed tool; ivi-cli
  never bundles or installs drivers (the stance ADR 0048 §2 fixed for
  the host side holds for the mock side too).
- Linux: the in-kernel `vhci-hcd` client that ships with the kernel's
  own USB/IP support.

Once attached, the device enumerates like any USBTMC instrument: the
inbox class driver binds, every vendor VISA lists it as
`USB0::<vid>::<pid>::<serial>::INSTR`, and ivi-cli's own USB scanner
finds it like any other instrument.

### 5. Out of scope

- Tier C (gadget hardware) — stays open as a possible future step; this
  ADR neither commits to nor forecloses it.
- Isochronous transfers, multiple configurations or interfaces, USB 3
  features, and device firmware update surfaces.
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

**Cons**

- The USBTMC/USB488-over-URB surface is a substantial protocol
  implementation with its own state machines.
- Windows attach depends on a third-party client (usbip-win2); CI cannot
  exercise the attach step on hosted Windows runners, so end-to-end runs
  stay bench-side (Linux `vhci-hcd` in CI is a possible later step).

## Verification

Protocol layers are unit-tested with fakes (descriptor tables, USBTMC
framing codecs, URB dispatch, the USB488 control requests) — no kernel
attach in CI. The end-to-end path is verified once on a real host before
release, per the ADR 0047 policy: attach via usbip-win2, confirm the
device binds to the inbox USBTMC class driver, confirm a vendor VISA and
`ivicli visa scan` both enumerate it, run a query round-trip and the
IEEE 488.2 SRQ sequence against a scenario, and observe the SRQ arriving
through `ServiceRequestStream`.
