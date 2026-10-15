# 0055. Serial instruments (ASRL)

- Status: Accepted
- Date: 2026-10-15

## Context

ivi-cli cannot reach an instrument on a serial line. `VisaResource`
parses TCPIP, TCPIP SOCKET, USB and GPIB, so `visa add` rejects an
`ASRL…::INSTR` resource, and `DefaultBackendFactory` sends any resource it
does not recognise to the Fake backend.

A serial instrument is reached two ways in practice. A VISA runtime opens
`ASRL1::INSTR` and decides itself which port board 1 is; NI-VISA maps
`ASRL1` to `COM1` on Windows, and the mapping on Linux differs between
runtimes and is often absent. Without a runtime, the port is opened by
name, `/dev/ttyUSB0` or `COM3`. Many hosts ivi-cli runs on, Linux on arm64
among them, have no VISA runtime at all.

A serial line also lacks what the network and USB transports carry: it
has no message end of its own, no status byte, no service requests and no
device clear, and it carries settings (baud rate, parity) that a TCP
connection does not.

#239 records the requirements and the test plan this ADR answers.

## Decision

### 1. Two resource forms

`VisaResource` gains two cases:

- `AsrlPort(string Port)`: `ASRL/dev/ttyUSB0::INSTR`, `ASRLCOM3::INSTR`. The
  text between `ASRL` and `::INSTR` names the port.
- `AsrlBoard(int Board)`: `ASRL1::INSTR`. The text is digits only.

Each round-trips through its canonical text, and the class is always
`INSTR`. The port name is masked in logs, as a host name or a USB serial
number is (ADR 0017), because a stable name such as
`/dev/serial/by-id/usb-FTDI_…` carries the adapter's serial number.

### 2. Which backend opens it

| Resource | Opened by |
| --- | --- |
| `AsrlPort` | the Serial backend, with the named port |
| `AsrlBoard`, board registered (§3) | the Serial backend, with the registered port |
| `AsrlBoard`, board not registered | the Local backend, through the VISA runtime |
| `AsrlBoard`, neither registered nor a VISA runtime | none: opening fails with a message that shows the `visa asrl add` line |
| either, with a mock scenario bound to the device | the Fake backend, as for the other transports |

The Serial backend is a new `IviCli.Backends.Serial` on `System.IO.Ports`,
with no VISA runtime. There is no built-in mapping from board numbers to
ports: the mapping differs between operating systems and runtimes, and a
guessed port that happens to exist would talk to the wrong instrument.

`DefaultBackendFactory` names every resource case it handles and returns
`UnsupportedTransport` for any other, so a resource kind without a backend
fails instead of reaching the Fake backend silently.

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
  move a board, remove it first.
- A port that does not exist yet is accepted with a warning, since a USB
  adapter appears only while plugged in.
- `visa scan` lists the serial ports and, for one no board names, the
  `visa asrl add` line that would register it.

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
  Local backend as `ISerialSession` attributes.
- `visa add` takes them as options.

They live on the device rather than in the resource text because VISA's
resource syntax has no place for them, and a VISA runtime reads them as
session attributes too.

### 5. Termination is a device setting for every transport

Every device gains `read_termination` and `write_termination`, defaulting to
`"\n"`, the terminator every backend writes and reads today.

- They apply where the transport has no message end of its own: SOCKET and
  serial, whether opened by the Serial backend or through VISA.
- HiSLIP, VXI-11 and USBTMC end messages with END, so there the settings
  are ignored, and a device that sets them gets a warning when it is
  added or loaded.

### 6. What a serial line does not have

- Reading the status byte, the service-request stream and device clear
  return `BackendOperationNotSupported` (ADR 0041), on either backend.
- Trigger writes `*TRG`.
- A gateway that exposes a serial device answers those requests the way
  its protocol reports an unsupported operation.

### 7. A serial port is exclusive

- Opening a port that another process or session already holds fails with
  `TransportInUse`, which names the port.
- The pool (ADR 0038) closes a serial session when it is released instead
  of holding it idle, so another program can open the port between uses.
- Two devices that name the same port are two pool entries (ADR 0038 §2);
  while one holds the port, the other's open fails as above.

### 8. Timeouts

The Serial backend sets the port's read and write timeouts to the device
timeout and implements `IEnforcesDeviceTimeout`, so ADR 0053 applies as it
does to VXI-11 and VISA: its own timeout error arrives first, and the
decorator is the backstop. A port that timed out is closed and reopened
before the next operation (ADR 0053 §3), which discards a reply that
arrives late on a line that has no request identifiers.

### 9. Packaging

`System.IO.Ports` comes from NuGet with native shims for each runtime
identifier, so THIRD-PARTY-NOTICES lists them. The shims load only when a
port is opened, so the release smoke test opens a pseudo-terminal on each
Linux build.

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
4. Backend selection, including the factory's catch-all, and the Local
   backend's serial attributes.
5. The surfaces of §10, `visa scan`, and the documentation (README, PRD and
   its mirror, ADRs 0003, 0010, 0017, 0038 and 0049, the CHANGELOG).
6. The CDC-ACM run in the nightly NI-VISA job and the release smoke test.

## Alternatives

- **A default board mapping per operating system** (`ASRL1` is `COM1` or
  `/dev/ttyS0`). Matches NI-VISA on Windows, but USB adapters get
  unpredictable names on Linux, and a wrong guess reaches the wrong
  instrument instead of failing.
- **VISA only.** One backend fewer, but no serial support on hosts without
  a runtime, which includes most Linux and every arm64 host.
- **Serial settings in the resource text**, `ASRL1::INSTR?baud=19200`. Not
  VISA syntax: no other VISA program could read the resource.
- **A fixed terminator per backend.** Today's behaviour; an instrument that
  ends its replies with `\r` cannot be used.

## Consequences

- A resource kind with no backend now fails with `UnsupportedTransport`
  instead of answering from the Fake backend.
- A configuration with `[[asrl]]` or `[devices.serial]` cannot be read by
  an older ivi-cli.
- The pool holds no idle serial session, so each use of a serial device
  pays for opening the port.
- The serial settings are checked against what the port object reports
  and in the nightly CDC-ACM run; a pseudo-terminal has no baud rate or
  parity to check them against.
