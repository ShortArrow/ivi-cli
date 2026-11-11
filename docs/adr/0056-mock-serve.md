# 0056. `mock serve`: one command from a scenario to a listening mock

- Status: Draft

## Context

Making a scenario reachable by a client takes five commands from three
groups, after the scenario itself is authored:

```sh
ivicli visa add dut 'TCPIP0::127.0.0.1::INSTR'
ivicli mock scenario activate my-dmm --for dut
ivicli server add dmm-srv --type hislip --port 4880
ivicli server route add dmm-srv hislip0 dut
ivicli server start dmm-srv
```

`mock --help` shows none of the last three, and `visa add` requires a resource
that a mock does not use. A user who starts from `mock` cannot tell from the
help that the next step is in `visa`, or what address to give it. The address
a client connects to is the server's, not the one given to `visa add`.

Three forces are in tension:

- **The common case should be short.** Most users of a mock want one scenario
  at one address.
- **`server` serves more than mocks.** One gateway relays real instruments and
  mocks alike, and may hold both behind one listener (ADR 0026 §16). The
  container image and the nightly workflow depend on `server` as it is. Its
  servers and routes are part of the `config.toml` contract (ADR 0047 §2).
- **A mock must not become a route to an instrument.** A binding lives in the
  session, keyed by device name, and a device without a binding dials its
  resource. Reusing a device name that a user registered with a real address
  would put a mock on that device, and removing the binding would then let new
  connections reach the instrument.

## Decision

### 1. The command

```sh
ivicli mock serve my-dmm --type socket --port 5025
```

`mock serve <scenario>` takes `--type` (`socket`, `hislip`, `vxi11` or `usbip`),
`--port`, `--alias`, `--bind` with the same default as `server add`, and
`--profile`. It prints the device name it uses and how a client connects: the
VISA resource, or for `usbip` the attach step. It then runs in the foreground as
`server start` does.

`--profile` takes `usbtmc` (the default) or `cdc-acm`, as `server route add`
does, and sets the USB profile of the route for `--type usbip`. A route of any
other type gets the default, whichever of the two is given. Any other value is
refused for every type, as `server route add` refuses it.

`--port` defaults as in `server add`, and to 1024 for `vxi11`, which has no
default there. 1024 is the port the `ivicli` VXI-11 client dials when no
portmapper on the host gives it another, so that client reaches the mock through
a resource without a port unless another VXI-11 server is registered there.

The scenario must already exist; `mock serve` does not create one. The path from
nothing to a listening mock is `mock scenario create`, a `mock rule add` per
rule, and `mock serve`.

### 2. It composes the existing model

`mock serve` registers a device, a binding, a server and a route, and starts the
server. These are ordinary entries, so `server stop`, `server status` and `visa
list` work on them, and `mock received` works on the device when the server runs
with `IVICLI_CAPTURE`. `server` keeps its commands, and its gateways change only
as §5 says.

The device's resource is `MOCK::INSTR`, a resource kind with no transport. A
device with it answers from its scenario while a binding names one. Otherwise
opening it fails, and the log says that no scenario is bound, an error distinct
from the one for a transport no backend supports. A client sees what its protocol
reports for a failed open: a closed connection, a HiSLIP fatal error or a VXI-11
I/O error. The built-in routing never dials it, so removing the binding can
neither reach an instrument nor loop back into the mock's own gateway.

The device's name is the value of `--alias`, checked by the same rule as a name
given to `visa add`. Without `--alias` it is `mock-<type>-<port>`, such as
`mock-socket-5025`, which always satisfies that rule. A client reaches the device
by the server's address; the name is what `visa list` shows and what `mock
received` takes.

The server takes the same name as the device, so the device and the server are
reached by one name. Server names follow the same rule as device names, and
devices and servers are separate tables in `config.toml`, so the name is valid
for both and the two entries do not collide. `ivi-management-api` is refused,
since `api start` records its process under that server name. One name serves at
one address: a second `mock serve` with the same name and a different port fails.

The route's endpoint is fixed by the type. What `mock serve` prints names the
port, since only `socket` carries it in the resource. Without it, `hislip` and
`usbip` reach their well-known ports and `vxi11` asks a portmapper:

| `--type` | Endpoint | Printed for a client |
| --- | --- | --- |
| `socket` | the port, such as `5025` | `TCPIP::127.0.0.1::5025::SOCKET` |
| `hislip` | `hislip0` | `TCPIP::127.0.0.1::hislip0,<port>::INSTR` |
| `vxi11` | `inst0` | `TCPIP::127.0.0.1::inst0,<port>::INSTR` |
| `usbip` | `1-1` | the attach command for busid `1-1` at the server's port |

The VXI-11 gateway ignores the device name a client sends and answers from the
server's first route, so `inst0` is chosen because VISA sends it by default, not
because the gateway reads it. A server `mock serve` creates has only this route.
Another endpoint, such as `hislip1`, is added with `server route add`, after
which `mock serve` no longer reuses the server (§4).

### 3. A name in use is reused only when it is the same entry

A device name that already exists, given or by default, is reused only if the
device has the resource `MOCK::INSTR` and no binding to another scenario.
Anything else is an error that names the device and what differs, and nothing is
changed. `mock serve` therefore never binds a scenario to a device with a
transport, and never replaces another scenario's binding.

Only `mock serve` registers a `MOCK::INSTR` device: `visa add` and `visa scan
--add` refuse that resource. A `MOCK::INSTR` device is therefore one `mock serve`
made, which is what this section and §7 rely on, unless `config.toml` was edited
by hand.

### 4. An existing server must match

An existing server with that name is reused only if its type, bind address and
port match the request, and it has no route other than the type's endpoint to
the device, with the requested USB profile when the type is `usbip`. That route
is added, with the profile §1 gives it, if it is missing, as it is after `visa
remove` of the device. Otherwise the command fails, naming the entry and the
difference, and changes nothing, rather than serving at an address other than
the one requested. A server that passes serves as the one `mock serve` would
make, whoever made it.

A server with other routes holds several devices behind one listener, which
remains `server`'s. A VXI-11 gateway answers from the server's first route, which
may not be the mock's, and a server composed by hand before `mock serve` ran
holds routes the mock did not add and must not remove. A route added later with
`server route add` therefore makes the next `mock serve` under that name fail.

### 5. A gateway reads the bindings before it chooses a backend

Today a process reads the session's bindings at startup, and a gateway refreshes
them only after a connection's backend is chosen and opened. An unbound
`MOCK::INSTR` device fails when its backend is opened, so the refresh would never
run, and a later `mock scenario activate` would go unseen until the server
restarts. Each gateway therefore refreshes the bindings before it chooses a
connection's backend. This also puts the binding `mock serve` writes in effect
for the first connection.

The change reaches every device. An `activate` or `deactivate` made while a
server runs takes effect from the next connection, where today it takes effect
one connection later. A device with a real address that is bound after the server
started therefore answers its next connection from the mock, as ADR 0026 §17
intends a serving gateway to follow the session. Bindings are session state, not
`config.toml`, so this is outside the contract of ADR 0047 §2.

### 6. A running server is refused

If the server's recorded process is alive, `mock serve` fails and leaves
the record alone. `server start` overwrites the record and removes it when the
bind then fails, so a second run would report the running server as stopped.

### 7. `mock unserve` removes what it registered

`mock unserve <alias>` takes the name `mock serve` gave the device and the
server: the value of `--alias`, or the name it printed (§2). It removes the
device with that name and its binding, whatever scenario the binding names, and
the server with that name together with its routes. The scenario itself is kept.

It removes nothing, and fails, naming the entry at fault, unless all of these
hold:

- A device with that name exists and its resource is `MOCK::INSTR` (§3).
- The server with that name, if it exists, has one of the four types in §2's
  table and at most one route. If it has one, that route goes to that device,
  at the endpoint the table gives the server's type; for `socket`, the server's
  port.
- No other server has a route to that device.
- No process is recorded under that name, or the recorded one is not alive
  (§6), whether or not the server entry remains. A record that cannot be read
  fails this check.

If no server has that name, the device and its binding are still removed. A
server that fails these conditions, such as a listener that serves the mock
among other devices, makes the command fail and is left to `server`. A record
whose process is not alive is removed with the server. Nothing else changes,
except that the default device is cleared if it named that device, as `visa
remove` does.

A running server is refused because `server stop` finds a server only through
its `config.toml` entry. Once that entry is removed, the process can be stopped
only from where it runs, by the operating system, or after an entry with that
name is added again.

### Not decided here

- Several devices behind one listener, and real and mock devices together: these
  remain `server`'s.
- Whether the `server` command group is renamed or given an alias. Its name does
  not say what it serves.

## Consequences

- Reaching a mock takes `mock scenario create`, the rules and `mock serve`, with
  no resource to invent.
- Nothing is removed or renamed, and a `config.toml` that loads today behaves as
  before, since `MOCK::INSTR` is rejected today. This requires no minor version
  under ADR 0047 §2. `config.toml` does gain entries the user did not type.
- An older `ivicli` cannot load a `config.toml` holding a `MOCK::INSTR` device. It
  rejects the whole file, including every other device and server, with an
  invalid-resource error.
- `visa list` and its `--json` `resource` field show `MOCK::INSTR`, which is not a
  VISA resource string.
- `mock serve` does not end the behaviour of a device with a real address and a
  binding that is later removed. That path stays as ADR 0026 §16 and §17 describe.
  A mock composed with `visa add` and `server route add`, to sit behind a server
  with other devices, takes that path too, since its resource has a transport.
- If the binding of a `mock serve` device is removed, open connections and pooled
  backends keep answering from the mock backend, where an unmatched query echoes.
  Once the pooled backend is evicted, a new connection fails. A plugin whose
  matcher claims the resource takes the device whether or not a binding exists.
- Running `mock serve` again after `mock scenario deactivate` binds the scenario
  again, since the command's meaning is to serve that scenario.
- The guide, the PRD's command reference and its Japanese mirror, and the README
  table gain `mock serve` and `mock unserve` when they are implemented.

## Alternatives

| Design | Why it is not the design in force |
| --- | --- |
| Replace `server` with `bridge` (real instruments) and `mock serve` (mocks) | Gives up real and mock devices behind one listener, and needs the `config.toml`, container, nightly and documentation changes of a break. A running `bridge` would still see a binding added or removed, so it needs a further mechanism to keep mocks and instruments apart |
| Move `server` under `mock` as `mock server` | `server` also relays real instruments, which do not belong under `mock` |
| The placeholder `TCPIP0::127.0.0.1::INSTR` as the device's resource | An unbound device dials its resource, here a VXI-11 listener at 127.0.0.1. That listener may relay to an instrument, and for a `--type vxi11` mock it can be the mock's own gateway, which routes back to the same device |
| The placeholder plus a field in `config.toml` marking the device as a mock | An older `ivicli` ignores the field, and drops it when it writes `config.toml`, with no error. The device then dials the placeholder again. A plugin matcher sees only the resource, so it cannot tell such a device from the placeholder |
| A `MOCK::<scenario>::INSTR` resource kind naming the scenario | The binding already names the scenario, and two places to name it can disagree |
| `visa add --scenario` | Shortens only the first two commands, and puts the entry point in `visa` while users start in `mock` |
| Documentation and help text only | The steps can be listed from `mock --help`, but the placeholder and the five commands across three groups remain |
| `mock remove <alias>` as the removal command | Sits beside `mock scenario remove` and reads as removing a scenario |
| Compose the five handlers, ignoring already-exists errors, with no further rule | A name collision binds a mock onto a user's device, the first run fails unless a VXI-11 listener answers, and a second run reports a running server as stopped |
