# 0048. USB Instrument Support

- Status: Accepted
- Date: 2026-08-06

## Context

The PRD and README present `USB0::0x0699::0x0408::C012345::INSTR` resource
strings as a supported way to address an instrument, and the lower layers
honor that promise: `VisaResource.Usb` parses and round-trips through
config, API contracts, and log masking ([ADR 0017 §4](0017-security-boundaries.md)),
and the backend factory routes `Usb` to `LocalBackend`. What is missing is
everything a user actually notices:

- **No discovery.** Four scanners exist (Fake, LXI mDNS, SOCKET sweep,
  VXI-11 broadcast); none enumerates USB. `visa scan` can never find a
  USB instrument, even though [ADR 0008 §5](0008-multicast-strategy.md)
  already defines its auto-registration alias (`usb-<serial>`).
- **No vendor-free session path.** `LocalBackend` opens USB sessions only
  when a vendor VISA runtime (NI-VISA, Keysight VISA, …) is installed.
  Unlike LAN instruments — where the managed HiSLIP / VXI-11 / SOCKET
  backends speak the wire protocol themselves — a USB instrument is
  unreachable on a machine without a vendor install.

One constraint shapes the options: ivi-cli must not couple to any vendor
VISA implementation. The existing `ReflectionVisaSessionFactory` already
observes this — it binds at runtime, via reflection, to the IVI Foundation
VISA.NET shared component (`Ivi.Visa.GlobalResourceManager` /
`IMessageBasedSession`) and to nothing vendor-specific. Depending on that
abstraction is acceptable; depending on a particular vendor's SDK is not.

## Decision

USB support lands in two stages. Stage 1 closes the discovery gap through
the abstraction we already bind to; stage 2 makes USB a first-class
transport with no VISA runtime at all, matching how the LAN protocols are
implemented.

### 1. Stage 1 — discovery through the VISA.NET abstraction

A new `IBackendScanner` in `IviCli.Backends.Local` enumerates USB
instruments by calling `Ivi.Visa.GlobalResourceManager.Find("USB?*::INSTR")`
against the IVI Foundation's own `IviFoundation.Visa` NuGet package:

- The package is the abstraction itself — published by the foundation's
  verified nuget.org account, `lib/net6.0`, no vendor code inside.
  Vendor implementations are never referenced; the shared components
  discover the installed provider at runtime (the `VendorAssemblies`
  layout VISA.NET 7.2 introduced for modern .NET).
- Reflection over an installer-provided `Ivi.Visa.dll` cannot serve
  here: the installers place the shared components in the GAC and
  `Framework64` directories, which modern .NET's `Assembly.Load` never
  probes. `ReflectionVisaSessionFactory` keeps its reflective binding —
  which now resolves the package-provided assembly from the application
  directory — and moving it to direct calls is a follow-up.
- When no vendor implementation is registered — or the implementation
  reports "no resources found", which VISA surfaces as an exception —
  the scanner contributes nothing and `visa scan` completes from the
  other sources. `ivicli doctor` remains the place that reports runtime
  diagnostics.
- Discovered resources parse through `VisaResource.Parse` and register
  with the deterministic `usb-<serial>` alias from ADR 0008 §5.
- Session open is unchanged: `Usb → LocalBackend`, still through the
  abstraction only.

Stage 1 makes the documented UX real on machines that have any conforming
VISA runtime, at the cost of still requiring one.

### 2. Stage 2 — managed USBTMC backend (`IviCli.Backends.Usb`)

A new backend assembly speaks USBTMC (with the USB488 subclass for
`*STB?`-style control) directly over the OS USB stack — WinUSB on
Windows, libusb on Linux/macOS — through a managed binding library. No
VISA runtime is involved.

- Routing follows the LAN precedent: `Usb → UsbBackend ?? LocalBackend
  ?? fallback`, so the native path wins when present and the abstraction
  path remains as fallback.
- Native discovery enumerates USB device descriptors (class `0xFE`,
  subclass `0x03`) and merges with stage 1's results, deduplicated by
  serial number.
- **Driver binding is reported, never changed.** On Windows a USBTMC
  instrument is usually bound to a vendor kernel driver; a WinUSB-based
  backend cannot claim it. The backend detects this and returns an
  actionable error naming the bound driver. Rebinding (Zadig, `udev`
  rules) is the operator's decision, documented, not automated.
- The concrete binding library, the USBTMC framing details, and the
  per-OS setup guidance are decided in a follow-up ADR when stage 2
  starts; this ADR fixes only the strategy and the routing/precedence
  contract.

### 3. Out of scope

- GPIB keeps its current story (`LocalBackend` via the abstraction);
  a native GPIB path has no comparable managed route.
- Non-TMC USB protocols (raw bulk, vendor-specific) stay out.
- Per-instrument quirk tables are a stage-2 implementation concern.

## Consequences

**Pros**

- Stage 1 is small, reuses the established reflection discipline, and
  makes `visa scan` match the PRD on any conforming VISA install.
- The vendor-coupling ban becomes explicit and testable instead of a
  convention buried in one factory's remarks.
- Stage 2 extends the "we speak the protocol ourselves" property from
  LAN to USB, which is what the repository's backend architecture
  already promises.

**Cons**

- Until stage 2 ships, USB still requires a vendor VISA runtime at
  runtime — the docs must say so plainly.
- USBTMC over WinUSB/libusb carries real driver-binding friction on
  Windows; some users will hit the "bound to vendor driver" error first.
- Self-contained release artifacts now bundle `Ivi.Visa.dll` under the
  IVI Foundation license (object-code use; free sublicense shipped with
  a product is expressly permitted, and ivi-cli charges nothing).

**Mitigations**

- PRD/README gain a one-line qualifier with stage 1 ("USB requires an
  installed VISA runtime") and drop it when stage 2 ships.
- The stage-2 error message names the exact device and driver and links
  the setup documentation.

## Verification

Per the [ADR 0047](0047-quality-assurance-and-support-scope.md) policy,
CI never talks to hardware; unit tests drive both stages through fake
factories (the existing `IVisaSessionFactory` seam, and a corresponding
seam for the USB stack in stage 2). Each stage is additionally verified
once against a real USBTMC instrument before its release: stage 1 on a
machine with a vendor VISA runtime (scan finds the instrument, the
`usb-<serial>` alias registers, a query round-trips), stage 2 on the same
instrument bound to WinUSB with no VISA runtime present.
