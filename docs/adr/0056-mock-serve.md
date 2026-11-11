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
`--port`, `--alias`, and `--bind` with the same default as `server add`. It
prints the device name it uses and how a client connects: the VISA resource, or
for `usbip` the attach step. It then runs in the foreground as `server start` does.

The scenario must already exist; `mock serve` does not create one. The path from
nothing to a listening mock is `mock scenario create`, a `mock rule add` per
rule, and `mock serve`.

### 2. It composes the existing model

`mock serve` registers a device, a binding, a server and a route, and starts the
server. These are ordinary entries, so `server stop`, `server status` and `visa
list` work on them, and `mock received` works on the device when the server runs
with `IVICLI_CAPTURE`. `server` keeps its commands and behaviour.

The device's resource is `MOCK::INSTR`, a resource kind with no transport. A
device with it answers from its scenario while a binding names one, and otherwise
fails with an error saying that no scenario is bound. It never dials, so removing
the binding can neither reach an instrument nor loop back into the mock's own
gateway.

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
port, since only `socket` carries it in the resource and the others reach the
well-known port otherwise:

| `--type` | Endpoint | Printed for a client |
| --- | --- | --- |
| `socket` | the port, such as `5025` | `TCPIP::127.0.0.1::5025::SOCKET` |
| `hislip` | `hislip0` | `TCPIP::127.0.0.1::hislip0,<port>::INSTR` |
| `vxi11` | `inst0` | `TCPIP::127.0.0.1::inst0,<port>::INSTR` |
| `usbip` | `1-1` | the attach command for busid `1-1` at the server's port |

The VXI-11 gateway ignores the device name a client sends and answers from the
server's first route, so `inst0` is chosen because VISA sends it by default, not
because the gateway reads it. A server `mock serve` creates has only this route.
Another endpoint, such as `hislip1`, is added with `server route add`.

### 3. A name in use is reused only when it is the same entry

A device name that already exists, given or by default, is reused only if the
device has the resource `MOCK::INSTR` and no binding to another scenario.
Anything else is an error that names the device and what differs, and nothing is
changed. `mock serve` therefore never binds a scenario to a device with a
transport, and never replaces another scenario's binding.

### 4. An existing server must match

An existing server with that name is reused only if its type, bind address
and port match the request, and its route for the type's endpoint only if the
route's device and USB profile match. Otherwise the command fails, naming the entry
and the difference, rather than serving at an address other than the one requested.

### 5. A gateway reads the bindings before it chooses a backend

Today a process reads the session's bindings at startup, and a gateway refreshes
them only after a connection's backend is chosen and opened. A `MOCK::INSTR`
device that is unbound fails at that choice, so the refresh would never run, and
a later `mock scenario activate` would go unseen until the server restarts. Each
gateway therefore refreshes the bindings before it chooses a connection's
backend. This also puts the binding `mock serve` writes in effect for the first
connection.

### 6. A running server is refused

If the server's recorded process is alive, `mock serve` fails and leaves
the record alone. `server start` overwrites the record and removes it when the
bind then fails, so a second run would report the running server as stopped.

### 7. What it registered can be removed

A command removes the device, the binding and the server that one `mock serve`
registered, and the routes that belong to them. Removing a device or a server
cascades to its routes, including any added by hand, and no other entry changes.
Its name is an open question.

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
- If the binding of a `mock serve` device is removed, open connections and pooled
  backends keep answering from the mock backend, where an unmatched query echoes.
  Once the pooled backend is evicted, a new connection fails. A plugin whose
  matcher claims the resource takes the device whether or not a binding exists.
- Running `mock serve` again after `mock scenario deactivate` binds the scenario
  again, since the command's meaning is to serve that scenario.
- The guide, the PRD's command reference and its Japanese mirror, and the README
  table gain the command when it is implemented.

## Alternatives

| Design | Why it is not the design in force |
| --- | --- |
| Replace `server` with `bridge` (real instruments) and `mock serve` (mocks) | Gives up real and mock devices behind one listener, and needs the `config.toml`, container, nightly and documentation changes of a break. A running `bridge` would still see a binding added or removed, so it needs a further mechanism to keep mocks and instruments apart |
| Move `server` under `mock` as `mock server` | `server` also relays real instruments, which do not belong under `mock` |
| The placeholder `TCPIP0::127.0.0.1::INSTR` as the device's resource | An unbound device dials its resource, here a VXI-11 listener at 127.0.0.1. That listener may relay to an instrument, and for a `--type vxi11` mock it is usually the mock's own gateway, which routes back to the same device |
| The placeholder plus a field in `config.toml` marking the device as a mock | An older `ivicli` ignores the field, and drops it when it writes `config.toml`, with no error. The device then dials the placeholder again. A plugin matcher sees only the resource, so it cannot tell such a device from the placeholder |
| A `MOCK::<scenario>::INSTR` resource kind naming the scenario | The binding already names the scenario, and two places to name it can disagree |
| `visa add --scenario` | Shortens only the first two commands, and puts the entry point in `visa` while users start in `mock` |
| Documentation and help text only | The steps can be listed from `mock --help`, but the placeholder and the five commands across three groups remain |
| Compose the five handlers, ignoring already-exists errors, with no further rule | A name collision binds a mock onto a user's device, the first run fails unless a VXI-11 listener answers, and a second run reports a running server as stopped |

## Open questions

- Whether `visa add` accepts `MOCK::INSTR` typed by hand.
- Whether `--port` is required for `vxi11`, which has no default in `server add`.
- Whether §4 reuses a VXI-11 server that already holds other routes. Its gateway
  answers from the first route, which may not be the one `mock serve` added.
- Whether `usbip` needs `--profile` here.
- The spelling of the removal command in §7, how it names what to remove now that
  the device name need not come from the scenario, and whether it refuses while
  the server runs.
- Whether §4 is enough to tell a server `mock serve` made from one a user made by
  hand with the same values.
