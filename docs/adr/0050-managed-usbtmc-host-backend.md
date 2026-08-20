# 0050. Managed USBTMC host backend

- Status: Accepted
- Date: 2026-08-20

## Context

[ADR 0048](0048-usb-instrument-support.md) split USB instrument support
into two stages and deferred three questions to a follow-up ADR: which
binding library stage 2 uses, how the USBTMC framing is implemented, and
what the per-OS setup looks like. Stage 1 shipped in 0.3.0-beta.1 —
`visa scan` enumerates USB instruments and `LocalBackend` talks to them,
both through whatever conforming VISA runtime the machine has.

Two facts have changed since 0048 was written, and both bear on the
deferred questions:

- The USB/IP device server shipped ([ADR 0049](0049-virtual-usb-mock-instrument.md)).
  A managed host stack and ivi-cli's own USBTMC device can now be run
  against each other on one machine, so stage 2 is developable and
  testable without a bench instrument — 0049's consequences section
  anticipated exactly this pairing.
- The dependency closure now has a maintained attribution file and a CI
  check against the lock file ([ADR 0046](0046-licensing.md)). A new
  dependency is a licensing decision, not just a technical one.

### What stage 2 buys over "install a VISA runtime"

The honest alternative is stage 1 plus a vendor install, so the case for
stage 2 rests on the machines where that alternative fails:

- **Linux beyond x86_64, and containers.** NI-VISA supports a short list
  of x86_64 distributions; Keysight IO Libraries are Windows-only. On an
  arm64 SBC driving a bench, or in a container, there is no runtime to
  install — stage 1 is not degraded there, it is impossible.
- **CI and e2e.** With stage 2, a gateway test can drive query + SRQ
  against the ADR 0049 mock with no vendor software in the image.
- **Footprint.** A vendor runtime is a multi-hundred-MB privileged
  install to run one query. ivi-cli's LAN transports already avoid this
  by speaking the protocol themselves; USB is the remaining exception.

On a Windows workstation that already has NI-VISA, stage 2 buys nothing —
stage 1 covers it, and instruments bound to the vendor's kernel driver
stay on that path (see §2).

## Decision

### 1. Access layer: OS facilities, no new packages

- **Windows: WinUSB via P/Invoke.** Enumeration through
  SetupAPI/CfgMgr32, transfers through `winusb.dll` — all inbox
  components. The backend implements USBTMC framing itself over the
  bulk pipes and reads USB488 notifications from the interrupt-IN pipe.
- **Linux: the kernel's own `usbtmc` class driver** via `/dev/usbtmc*`.
  The kernel does the framing, bTag discipline, and quirk handling;
  USB488 control (`READ_STATUS_BYTE`) and SRQ delivery are exposed as
  ioctls and `poll()` on the same descriptor. The backend is file I/O
  plus a small ioctl surface — no user-space USB stack at all. This
  means Linux support tracks distro kernels that ship the module; the
  Microsoft WSL kernel does not (measured 2026-08-17, ADR 0049
  §Verification), so WSL is served by the LAN transports instead.
- **macOS: out of scope.** No usbtmc character device exists there, so
  macOS would need the libusb path rejected below, and the ADR 0047
  policy supports only what a release check verifies.

**LibUsbDotNet is rejected**, although it is the obvious single-path
alternative (one code path for every OS, WinUSB supported underneath).
It is licensed LGPL-3.0-or-later, and libusb itself is LGPL-2.1. Both
could be carried in THIRD-PARTY-NOTICES.md, but every license in the
closure today is IVI, MIT, BSD, or Apache-2.0, and an LGPL-3.0 component
in the self-contained archives would add relink-permission obligations
those licenses do not have. Zero new packages beats one code path,
because the two paths that remain are thin: P/Invoke of inbox DLLs on
Windows, file I/O on Linux.

### 2. One backend; the bound driver picks the path per device

ADR 0048 fixed assembly-level routing: `Usb → UsbBackend ?? LocalBackend
?? fallback`, with `IviCli.Backends.Usb` as its own assembly. This ADR
refines what `UsbBackend` does per device, and the deciding fact is
**which driver owns the instrument's USBTMC interface**:

| OS | Interface owned by | Path |
| --- | --- | --- |
| Windows | WinUSB | `UsbBackend` (this ADR) |
| Windows | vendor USBTMC kernel driver (e.g. NI `Usbtmc`) | `LocalBackend` through the vendor runtime — a WinUSB handle cannot be opened on a claimed interface |
| Windows | nothing | error naming the device and the WinUSB binding step |
| Linux | `usbtmc` module | `UsbBackend` — the claim *is* the path |
| Linux | nothing (module absent) | error naming the module |

The 0048 stance stands: **binding is reported, never changed.** The
backend does not rebind a Windows driver and does not detach a Linux
kernel driver; on Linux the question dissolves because the kernel
driver's character device is the interface the backend uses. No new
configuration key is added — the table above is decidable from device
state at open time, and an operator who wants the other path changes the
binding with OS tools, which the error message names.

### 3. Framing lives where it already lives

`IviCli.Domain.Protocols.UsbTmcConstants` already carries the USBTMC and
USB488 message IDs, header layout, and capability bits, and the ADR 0049
device server exercises them from the device side under test. The
Windows path reuses those constants host-side (`DEV_DEP_MSG_OUT` /
`REQUEST_DEV_DEP_MSG_IN` over the bulk pipes, the interrupt-IN
notification format for SRQ). The Linux path never sees a USBTMC header
— the kernel owns the framing — so it uses only the ioctl surface. The
transport behind `UsbBackend` sits behind a seam
(`IUsbTmcTransport`-shaped, like `IVisaSessionFactory` in the Local
backend) so unit tests drive the backend without hardware.

### 4. Verification

Per ADR 0047, CI never touches a kernel USB stack. The release-gating
evidence, on one machine each:

- **Windows:** export the ADR 0049 mock, bind the attached device to
  WinUSB (no VISA runtime present), then query round-trip, `*TRG`, and
  a rule-raised SRQ through `UsbBackend`.
- **Linux (distro kernel, not WSL):** attach the mock through
  `vhci-hcd`, confirm `usbtmc` binds and `/dev/usbtmc0` appears, same
  three legs through the character device.
- One run against a real USBTMC instrument before the first release
  that ships the backend, as 0048 already requires.

## Consequences

- The dependency closure is unchanged; THIRD-PARTY-NOTICES.md is
  untouched, and the reason LibUsbDotNet is absent is recorded here
  rather than rediscovered at every dependency review.
- Two OS paths share only the Domain constants. That is more per-OS
  code than a libusb single path — the price of zero LGPL surface and
  of letting the Linux kernel keep owning quirks it already handles.
- Windows keeps its rebinding friction: a user whose instrument is
  bound to a vendor driver either uses that vendor's runtime through
  stage 1 or rebinds to WinUSB deliberately. The error message carries
  the driver name and the documentation link (0048's mitigation).
- The implementation is post-0.3.0 work; this ADR exists so the stage-2
  branch starts from decisions instead of research.
