# 0055. Serial instruments (ASRL)

- Status: Accepted
- Date: 2026-10-15

## Context

ivi-cli cannot reach an instrument on a serial line. `VisaResource`
parses TCPIP, TCPIP SOCKET, USB and GPIB, so `visa add` rejects an
`ASRL…::INSTR` resource, and `DefaultBackendFactory` sends any resource
case it does not name to the Fake backend.

A serial instrument is reached two ways in practice. A VISA runtime opens
`ASRL1::INSTR` and decides itself which port board 1 is; NI-VISA maps
`ASRL1` to `COM1` on Windows, and the mapping on Linux differs between
runtimes and is often absent. Without a runtime, the port is opened by
name, `/dev/ttyUSB0` or `COM3`. Many hosts ivi-cli runs on have no VISA
runtime.

A serial line also lacks what the network and USB transports carry: it
has no message end of its own and no service requests, and it carries
settings, such as the baud rate and parity, that a TCP connection does
not.

#239 records the requirements and the test plan this ADR answers. Where
this ADR departs from #239, the Alternatives say why. It also settles
points #239 left open: how a port is compared (§3), DTR (§4), the allowed
terminators (§5), how a late reply is handled (§8), and which further
documents change (§11).

## Decision

### 1. Two resource forms

`VisaResource` gains two cases:

- `AsrlPort(string Port)`: `ASRL/dev/ttyUSB0::INSTR`, `ASRLCOM3::INSTR`. The
  text between `ASRL` and `::INSTR` names the port.
- `AsrlBoard(int Board)`: `ASRL1::INSTR`. The text is digits only, and
  `ASRL::INSTR` is board 0, as an empty board is for the other resource
  kinds. A port whose name is only digits therefore cannot be named.

Each round-trips through its canonical text, and the class is always
`INSTR`. The port name is masked in logs, as a host name or a USB serial
number is (ADR 0017), because a stable name such as
`/dev/serial/by-id/usb-FTDI_…` carries the adapter's serial number.

### 2. Which backend opens it

| Resource | Opened by |
| --- | --- |
| either, with a mock scenario bound to the device, or with `IVICLI_MOCK_ONLY` set | the Fake backend, as for SOCKET, USB and GPIB |
| `AsrlPort` | the Serial backend, with the named port |
| `AsrlBoard`, board registered (§3) | the Serial backend, with the registered port |
| `AsrlBoard`, board not registered | the Local backend, through the VISA runtime |

The first row that matches wins. When an unregistered board cannot be
opened, because there is no VISA runtime or the runtime does not know the
board, the error shows the `visa asrl add` line that registers it.

The Serial backend is a new `IviCli.Backends.Serial` on `System.IO.Ports`,
with no VISA runtime. There is no built-in mapping from board numbers to
ports: the mapping differs between operating systems and runtimes, and a
guessed port that happens to exist would talk to the wrong instrument.

`DefaultBackendFactory` names every resource case it handles and returns
`UnsupportedTransport` for a case it does not name, instead of the Fake
backend. A case it names but whose backend is not registered still falls
back to the Fake backend, which is how `IVICLI_MOCK_ONLY` works.

### 3. Board numbers are registered per machine

Which port board 1 means belongs to the machine, not to a device, so it is
a table of its own in `config.toml`:

```toml
[[asrl]]
board = 1
port = "/dev/ttyUSB0"
```

`ivicli visa asrl add <board> <port>`, `visa asrl list` (with `--json`) and
`visa asrl remove <board>` manage it.

- Registering a board twice, or one port under two boards, is an error; to
  move a board, remove it first. Ports are compared by the file they
  resolve to, so a `/dev/serial/by-id/` link and the `/dev/ttyUSB0` it
  points at are the same port.
- A port that does not exist yet is accepted with a warning, since a USB
  adapter appears only while plugged in.
- `visa scan` lists the serial ports and, for one no board names, the
  `visa asrl add` line that would register it.

The factory reads its configuration once, at start, so an `AsrlBoard`
device gets a backend that reads the table from the configuration store
when it opens the session and then hands the session to the Serial or the
Local backend. A running gateway therefore sees a board registered after
it started, from the next open; a session the pool already holds keeps the
backend it was opened with until it is closed.

### 4. Serial settings are device properties

An ASRL device carries serial settings beside its resource and timeout:

```toml
[devices.serial]
baud = 9600            # default
data_bits = 8          # default; 5 to 8
parity = "none"        # none | odd | even | mark | space
stop_bits = 1          # 1 | 1.5 | 2
flow_control = "none"  # none | xon_xoff | rts_cts
```

- The defaults are VISA's, 9600 8-N-1 without flow control. Only values
  that differ from them are written.
- A `[devices.serial]` table on a device that is not ASRL is a
  configuration error.
- The Serial backend applies them as `SerialPort` properties, and the
  Local backend as `ISerialSession` attributes. A value the platform's
  serial driver cannot set fails the open, naming the setting when the
  driver says which; the driver's own message is not passed on, since it
  can carry the port's name.
- Both backends assert DTR while the port is open: the Serial backend
  through `SerialPort.DtrEnable`, the Local backend through
  `VI_ATTR_ASRL_DTR_STATE`. An instrument that needs DTR to talk then
  behaves the same on both. The Serial backend raises DTR after opening
  the port, and a port with no modem lines to raise it on, such as a
  pseudo-terminal, is used without it: `SerialPort` fails its open when
  asked for DTR there.
- `visa add` takes them as options.

They live on the device rather than in the resource text because VISA's
resource syntax has no place for them, and a VISA runtime reads them as
session attributes too.

### 5. Termination is a device setting

Every device may set `read_termination` and `write_termination`, each one
of `"\n"`, `"\r"` or `"\r\n"`.

- They apply where the transport has no message end of its own: SOCKET and
  serial, whether opened by the Serial backend or through VISA.
- HiSLIP, VXI-11 and USBTMC end messages with END, and GPIB with EOI, so
  there the settings are ignored, and a device that sets them gets a
  warning when it is added or loaded.
- Unset, nothing changes from today. Writes end with `"\n"`. A SOCKET read
  ends at LF, CR or CRLF; a read through VISA ends at LF or END, with a
  trailing CR removed. The Serial backend, which is new, reads as VISA
  does, so a serial device behaves the same on both paths.
- Set, writes end with `write_termination`. The Serial and SOCKET backends
  end a read at exactly `read_termination`. VISA ends a read at one
  character, the last of `read_termination`, so with `"\r\n"` a reply that
  ends in a bare LF also ends the read; the preceding CR is removed when
  it is there. The Local backend then reads up to that character itself
  rather than through a line read.

### 6. What a serial line does not have

- Trigger writes `*TRG`.
- The service-request stream ends at once, as for any backend without
  service requests (ADR 0041 §1).
- Nothing changes in the gateways. `IIviBackend` has no status-byte read or
  device clear: a gateway answers device clear by clearing its own
  buffers, and the USB488 status byte from the service requests it has
  seen, for every backend. A serial device raises none, so its status byte
  never shows RQS.

### 7. A serial port is exclusive

- Opening a port that the operating system reports as busy fails with a
  new `BackendError`, `TransportInUse`, which names the resource, masked
  in logs. A VISA open of any resource that another session holds or has
  locked fails with the same error. Whether a port another program already
  has open is reported as busy depends on the platform and that program:
  Windows always does, while on Linux and macOS only a port opened for
  exclusive use is.
- The pool (ADR 0038) holds a serial session as it holds a HiSLIP, VXI-11,
  SOCKET or VISA one: after release it stays open until it has been idle
  for `[pool] idle_timeout`, 60 seconds by default, or is evicted to stay
  under `max_devices` (ADR 0038 §4). Meanwhile no other program can open
  the port.
- Two devices that name the same port are two pool entries (ADR 0038 §2);
  while one holds the port, the other's open fails as above.

### 8. Timeouts

The Serial backend sets the port's read and write timeouts to the device
timeout and implements `IEnforcesDeviceTimeout`, so ADR 0053 applies as it
does to VXI-11 and VISA: its own timeout error arrives first, and the
decorator is the backstop. Its blocking port calls run off the caller's
thread, so the decorator can abandon one that does not return.

A line has no request identifiers, so a reply that arrives after its
request timed out could be read as the answer to the next one. Closing and
reopening the port after a timeout (ADR 0053 §3) discards what had already
arrived; a reply that arrives after the reopen is still read as the next
answer.

### 9. Packaging

`System.IO.Ports` comes from NuGet with native shims for each runtime
identifier, so THIRD-PARTY-NOTICES lists them. The shims load only when a
port is opened, so the release smoke test opens a pseudo-terminal with
each Linux and macOS build and with the NativeAOT image.

### 10. Surfaces

`visa list` and its JSON, the API's `DeviceDto`, `visa scan --add` and the
log masking learn the serial settings and the termination fields.

### 11. Delivery

Each step is a pull request that leaves `main` releasable:

1. Domain and configuration: the resource cases, serial settings,
   termination, the `[[asrl]]` table.
2. The `visa asrl` commands and the `visa add` options.
3. The Serial backend, tested over a socat pseudo-terminal pair in Linux
   CI.
4. Backend selection, including the factory's catch-all and the board
   lookup at open, and the Local backend's serial attributes and
   termination.
5. The surfaces of §10, `visa scan`, and the documentation: README, PRD
   and its mirror, ADRs 0003, 0010, 0017, 0038 and 0049, the CHANGELOG, and
   `docs/conformance.md`, whose timeout entries say a late reply is never
   read, which §8 does not hold for a serial line.
6. The CDC-ACM run in the nightly NI-VISA job and the release smoke test.

## Alternatives

- **A default board mapping per operating system** (`ASRL1` is `COM1` or
  `/dev/ttyS0`). Matches NI-VISA on Windows, but USB adapters get
  unpredictable names on Linux, and a wrong guess reaches the wrong
  instrument instead of failing.
- **VISA only.** One backend fewer, but no serial support on a host
  without a runtime.
- **Serial settings in the resource text**, `ASRL1::INSTR?baud=19200`. Not
  VISA syntax: no other VISA program could read the resource.
- **A strict `"\n"` default for termination**, as #239 proposed. A SOCKET
  read ends at CR today, so an instrument that replies with `"\r"` over
  SOCKET works now and would stop working until its device set the
  terminator.
- **Closing a serial session when the pool releases it**, as #239
  proposed, so another program can open the port between uses. On Linux
  closing a port drops DTR, which resets some instruments, such as
  microcontroller boards, on every use, and each use pays for opening the
  port.
- **Reporting the status byte, service requests and device clear as
  `BackendOperationNotSupported`**, as #239 proposed. The backend port has
  no status-byte read or device clear to report it from, and the gateways
  already answer both without the backend.
- **Discarding the input buffer before each write**, to narrow the window
  for a late reply further. A client that writes two queries before
  reading the first answer would lose that answer, which works over
  SOCKET today.

## Consequences

- A resource case with no backend now fails with `UnsupportedTransport`
  instead of answering from the Fake backend.
- A configuration with `[[asrl]]`, `[devices.serial]` or a termination
  field cannot be read by an older ivi-cli.
- While the pool holds a serial port, other programs on the machine cannot
  open it; a shorter pool idle timeout gives it back sooner.
- A late reply on a serial line can still be read as the next answer.
- DTR/DSR handshaking is not offered: `SerialPort` has no such mode.
- The serial settings are checked against what the port object reports
  and in the nightly CDC-ACM run; a pseudo-terminal has no baud rate or
  parity to check them against.
