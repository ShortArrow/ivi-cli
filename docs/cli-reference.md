# Command reference

Every `ivicli` command, argument and option, generated from the command tree by
`CliReferenceTests`. Edit the descriptions in the command's source and run the
tests with `IVICLI_UPDATE_CLI_REFERENCE=1` to rewrite this page.

## `ivicli`

ivi-cli: integrated CLI for managing, diagnosing, and operating VISA/IVI instruments.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--verbose` | `-v` | no | 0..1 | Log at Debug and above. (applies to every subcommand) |
| `-vv` |  | no | 0..1 | Log at Trace and above. (applies to every subcommand) |
| `--quiet` | `-q` | no | 0..1 | Suppress console output below Warning; the log file is unaffected. (applies to every subcommand) |
| `--log-format` |  | no | 1 | Console log format: human (default) or json. (applies to every subcommand) |
| `--log-file` |  | no | 1 | Write the log file here instead of the default location. (applies to every subcommand) |

## `ivicli visa`

VISA transport / SCPI operations.

## `ivicli visa add`

Register a new device alias.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | The device alias to register (for example psu1). |
| `resource` | 1 | The VISA resource string (for example TCPIP0::host::inst0::INSTR or ASRL/dev/ttyUSB0::INSTR). |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--timeout-ms` |  | no | 1 | Per-device default operation timeout, in milliseconds. |
| `--baud` |  | no | 1 | Serial baud rate (ASRL only; default 9600). |
| `--data-bits` |  | no | 1 | Serial data bits, 5 to 8 (ASRL only; default 8). |
| `--parity` |  | no | 1 | Serial parity: none, odd, even, mark or space (ASRL only; default none). |
| `--stop-bits` |  | no | 1 | Serial stop bits: 1, 1.5 or 2 (ASRL only; default 1). |
| `--flow-control` |  | no | 1 | Serial flow control: none, xon_xoff or rts_cts (ASRL only; default none). |
| `--read-termination` |  | no | 1 | What ends a reply on SOCKET and serial: lf, cr or crlf (or \n, \r, \r\n). |
| `--write-termination` |  | no | 1 | What ends a command on SOCKET and serial: lf, cr or crlf (or \n, \r, \r\n). |

## `ivicli visa remove`

Remove a registered device alias and its mock scenario binding.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | The device alias to remove. |

## `ivicli visa list`

List configured devices.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON instead of the human-readable table. |

## `ivicli visa use`

Set the current device target.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | The device alias to make current. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--default` |  | no | 0..1 | Also persist the alias as the default device in config.toml. |

## `ivicli visa current`

Show the currently selected device.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |

## `ivicli visa scan`

Enumerate VISA resources visible to the registered backends (LXI mDNS + VXI-11 portmapper broadcast, plus optional --port TCP sweep, ADR 0008).

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |
| `--add` |  | no | 0..1 | Auto-register every discovered resource via `visa add` (alias = host portion of the VISA resource). Existing names are skipped. |
| `--add-timeout-ms` |  | no | 1 | Per-device default operation timeout for auto-registered entries (default 3000). |
| `--port` |  | no | 1..100000 | TCP-sweep the local subnet for raw-SOCKET instruments on this port (repeatable, e.g. --port 5025 --port 1394). Also probed on every discovered host. Without this flag only broadcast/mDNS discovery runs. |
| `--subnet` |  | no | 1 | Sweep this CIDR (e.g. 192.168.3.0/24) instead of the auto-detected local subnets. |
| `--host` |  | no | 1 | Probe only this single host (no subnet sweep). |
| `--verbose` |  | no | 0..1 | Send *IDN? to each discovered SOCKET endpoint to report the model, and show diagnostics such as the resolved VXI-11 Core port. |

## `ivicli visa asrl`

Map serial board numbers (ASRL<n>::INSTR) to ports.

## `ivicli visa asrl list`

List the registered board numbers.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit the listing as JSON. |

## `ivicli visa asrl add`

Register the port a board number means on this machine.

| Argument | Arity | Description |
| --- | --- | --- |
| `board` | 1 | The board number, as in ASRL<board>::INSTR. |
| `port` | 1 | The port, for example /dev/ttyUSB0 or COM3. |

## `ivicli visa asrl remove`

Remove a board number.

| Argument | Arity | Description |
| --- | --- | --- |
| `board` | 1 | The board number to remove. |

## `ivicli visa query`

Send a SCPI query and print the response.

| Argument | Arity | Description |
| --- | --- | --- |
| `name-or-scpi` | 1 | Either the device alias or the SCPI text. See examples in --help. |
| `scpi` | 0..1 | The SCPI query when an explicit alias is supplied as the first argument. |

## `ivicli visa write`

Send a SCPI command (no response expected).

| Argument | Arity | Description |
| --- | --- | --- |
| `name-or-scpi` | 1 | Either the device alias or the SCPI text. See examples in --help. |
| `scpi` | 0..1 | The SCPI command when an explicit alias is supplied as the first argument. |

## `ivicli visa read`

Read any pending response from the current (or named) device.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 0..1 | Optional device alias. Defaults to the current device. |

## `ivicli visa status`

Show connection state, response time, and IDN for the device.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 0..1 | Optional device alias. Defaults to the current device. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |

## `ivicli visa script`

Execute a SCPI script file against the active device.

| Argument | Arity | Description |
| --- | --- | --- |
| `file` | 1 | Path to a SCPI script file: !sleep, !assert, !values, !echo and !# are directives, every other line is sent as written. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--device` |  | no | 1 | Target device alias (defaults to the session-current device). |

## `ivicli visa monitor`

Poll a SCPI query at a fixed interval and stream timestamped responses.

| Argument | Arity | Description |
| --- | --- | --- |
| `query` | 1 | SCPI query to repeat (a header must end with `?`). |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--device` |  | no | 1 | Target device alias (defaults to the session-current device). |
| `--interval` |  | no | 1 | Poll interval in milliseconds (default 1000). |
| `--count` |  | no | 1 | Stop after this many samples (default: run until Ctrl+C). |
| `--json` |  | no | 0..1 | Emit one JSON object per sample instead of plain text. |

## `ivicli visa watch`

Watch registered devices in a live table, refreshed at a fixed interval.

| Argument | Arity | Description |
| --- | --- | --- |
| `devices` | 0..100000 | Device aliases to watch. Omit to watch every registered device. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--interval` | `-i` | no | 1 | Poll interval in milliseconds (default 1000). |
| `--count` | `-n` | no | 1 | Stop after this many ticks (default: run until Ctrl+C). |
| `--json` |  | no | 0..1 | Emit one NDJSON object per tick instead of a live table. |
| `--plain` |  | no | 0..1 | Emit ANSI-free plain-text snapshots per tick (suitable for CI / log capture). |

## `ivicli visa lint`

Check a SCPI script for unknown command roots without running it.

| Argument | Arity | Description |
| --- | --- | --- |
| `path` | 1 | Path to the .scpi script to lint. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit findings as a JSON array on stdout instead of plain text. |

## `ivicli mock`

Manage mock-device behaviour for the Fake Backend.

## `ivicli mock scenario`

Manage mock scenarios.

## `ivicli mock scenario list`

List every scenario.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |

## `ivicli mock scenario create`

Create an empty scenario.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Scenario name. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--idn` |  | no | 1 | Optional default *IDN? response for the scenario. |
| `--initial` |  | no | 1 | Initial scene name. When set, the scenario starts with that scene (empty, no rules) as its only scene; otherwise the synthetic `default` scene is used. |
| `--unmatched` |  | no | 1 | What a request no rule matches gets: `echo` answers a query with its own text and accepts a write; `fail` fails both. |

## `ivicli mock scenario remove`

Remove a scenario.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Scenario name. |

## `ivicli mock scenario show`

Show a scenario's scenes.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Scenario name. |

## `ivicli mock scenario activate`

Bind a scenario to a device.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Scenario name. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--for` |  | no | 1 | Device alias to bind the scenario to. Defaults to the current device (see `visa use`). |

## `ivicli mock scenario deactivate`

Clear a device's scenario binding.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--for` |  | no | 1 | Device alias whose scenario binding to clear. Defaults to the current device. |

## `ivicli mock scenario list-active`

List every device with an active scenario binding.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |

## `ivicli mock scenario record`

Replay a script against the active backend and record observed traffic into a scenario.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Scenario to record into. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--from-script` |  | yes | 1 | Script file whose queries/writes will be captured (ADR 0027 §4). |
| `--device` |  | no | 1 | Target device alias (defaults to session-current). |

## `ivicli mock scenario import`

Convert an NDJSON capture into a stored mock scenario (ADR 0033).

| Argument | Arity | Description |
| --- | --- | --- |
| `path` | 1 | Path to the NDJSON capture file (IVICLI_CAPTURE output). |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--name` |  | yes | 1 | Scenario name to store the imported scenes under. |
| `--device` |  | no | 1 | Device alias filter when the capture covers multiple devices (required in that case). |
| `--force` |  | no | 0..1 | Overwrite an existing scenario with the same name. |

## `ivicli mock scene`

Manage scene states within a scenario.

## `ivicli mock scene add`

Add a new empty scene to a scenario.

| Argument | Arity | Description |
| --- | --- | --- |
| `scenario` | 1 | The owning scenario. |
| `scene` | 1 | The new scene's alias. |

## `ivicli mock scene remove`

Remove a scene from a scenario by alias.

| Argument | Arity | Description |
| --- | --- | --- |
| `scenario` | 1 | The owning scenario. |
| `scene` | 1 | The scene alias to remove. |

## `ivicli mock rule`

Manage rules within a scenario's scenes.

## `ivicli mock rule add`

Append a rule to a scene inside a scenario.

| Argument | Arity | Description |
| --- | --- | --- |
| `scenario` | 1 | The owning scenario. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--in` |  | no | 1 | Target scene name. Defaults to the scenario's initial scene if omitted. |
| `--match` |  | yes | 1 | The exact SCPI text the rule reacts to (required). |
| `--respond` |  | no | 1 | Textual response (legal for queries). |
| `--ack` |  | no | 0..1 | Acknowledge with no response (legal for writes). |
| `--fail` |  | no | 1 | Surface a canned BackendError. Variant tags: transport_timeout, transport_disconnected. |
| `--fail-detail` |  | no | 1 | Detail payload for the fail variant (e.g. timeout ms). |
| `--transition-to` |  | no | 1 | After the rule fires, make this scene current. Target scene must exist in the scenario. |
| `--srq` |  | no | 1 | Status byte to raise a service request with when the rule fires (0..255, decimal or 0x hex). |

## `ivicli mock rule remove`

Remove a rule from a scene by 1-based index.

| Argument | Arity | Description |
| --- | --- | --- |
| `scenario` | 1 | The owning scenario. |
| `index` | 1 | 1-based rule index inside the target scene. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--in` |  | no | 1 | Target scene name. Defaults to the scenario's initial scene if omitted. |

## `ivicli mock received`

Report the SCPI writes a device received, read back from an IVICLI_CAPTURE traffic log.

| Argument | Arity | Description |
| --- | --- | --- |
| `device` | 1 | Device alias whose received writes to report. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--match` |  | no | 1 | Only writes whose SCPI contains this substring (e.g. ':VOLT'). |
| `--exact` |  | no | 1 | Only writes whose SCPI equals this exactly (e.g. ':VOLT 24.000'). Cannot combine with --match. |
| `--capture` |  | no | 1 | NDJSON capture path. Defaults to IVICLI_CAPTURE; a relative path resolves under the log directory. |
| `--all` |  | no | 0..1 | List every matching write (oldest first) instead of only the last. |
| `--count` |  | no | 0..1 | Print how many writes matched instead of the writes (exit 0 even at 0). |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |

## `ivicli mock serve`

Serve a scenario at one address, registering its device, server and route, and run the server in the foreground (Ctrl+C to stop).

| Argument | Arity | Description |
| --- | --- | --- |
| `scenario` | 1 | The scenario to serve (see `mock scenario list`). |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--type` |  | yes | 1 | How a client connects: socket, hislip, vxi11 or usbip. |
| `--port` |  | no | 1 | TCP port to listen on (defaults: socket=5025, hislip=4880, vxi11=1024, usbip=3240). |
| `--alias` |  | no | 1 | Name of the mock's device and server (default mock-<type>-<port>). |
| `--bind` |  | no | 1 | Bind address (default 127.0.0.1). |
| `--profile` |  | no | 1 | USB profile for --type usbip: usbtmc (default) or cdc-acm. |

## `ivicli mock unserve`

Remove the device, binding, server and route `mock serve` registered; the scenario is kept.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | The name `mock serve` printed, or the --alias given to it. |

## `ivicli server`

Manage gateway servers, routes, and the lifecycle of running listeners.

## `ivicli server list`

List configured gateway servers.

## `ivicli server add`

Register a new gateway server.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Server alias. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--type` |  | yes | 1 | Server type: local, socket, hislip, vxi11, usbip. |
| `--bind` |  | no | 1 | Bind address (default 127.0.0.1). |
| `--port` |  | no | 1 | TCP port to listen on (defaults: socket=5025, hislip=4880, usbip=3240). |

## `ivicli server remove`

Remove a registered gateway server (cascades routes).

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Server alias to remove. |

## `ivicli server route`

Manage server-to-device routes.

## `ivicli server route list`

List configured routes.

## `ivicli server route add`

Bind a public endpoint to a local device.

| Argument | Arity | Description |
| --- | --- | --- |
| `server` | 1 | Server alias. |
| `endpoint` | 1 | Public endpoint (hislip0 / 5025 etc.). |
| `device` | 1 | Device alias to bind to this endpoint. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--profile` |  | no | 1 | USB profile a usbip export presents: usbtmc (default; a VISA USB::…::INSTR resource) or cdc-acm (a COM port through the inbox serial driver). Ignored by every other server type. |

## `ivicli server route remove`

Remove a server-to-device route.

| Argument | Arity | Description |
| --- | --- | --- |
| `server` | 1 | Server alias. |
| `endpoint` | 1 | Public endpoint. |

## `ivicli server start`

Start a configured gateway server in the foreground (Ctrl+C to stop).

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Configured server name to start (see `server list`). |

## `ivicli server stop`

Send a termination signal to the process recorded in the server's PID file.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Configured server name whose process should be terminated. |

## `ivicli server status`

Report configured gateway servers and routes.

## `ivicli server log`

Tail the structured log file for a gateway server.

| Argument | Arity | Description |
| --- | --- | --- |
| `name` | 1 | Server alias whose log file should be tailed. |

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--follow` | `-f` | no | 0..1 | Keep reading appended lines until cancelled (default: dump and exit). |
| `--tail` |  | no | 1 | Show the last N lines before following (default 50). |

## `ivicli api`

Manage the local Management API (HTTP JSON).

## `ivicli api start`

Start the Management API listener in the foreground.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--port` |  | no | 1 | TCP port to bind on (default 8080 HTTP, 8443 HTTPS). |
| `--bind` |  | no | 1 | Listen address (default 127.0.0.1). |
| `--allow-anonymous` |  | no | 0..1 | Allow requests without an API token. Required when binding non-loopback with no tokens configured (ADR 0036). |
| `--tls` |  | no | 0..1 | Serve HTTPS instead of HTTP (ADR 0039). |
| `--tls-cert` |  | no | 1 | Path to the server certificate (PFX or PEM). |
| `--tls-key` |  | no | 1 | Path to the PEM private key (when --tls-cert is a PEM file). |
| `--tls-password-env` |  | no | 1 | Environment variable holding the PFX password. |
| `--tls-self-signed` |  | no | 0..1 | Generate an ephemeral self-signed cert (dev only). |
| `--tls-client-required` |  | no | 0..1 | Require client certificate (mTLS). |
| `--tls-client-ca` |  | no | 1 | Path to PEM bundle of trusted client CAs (required with --tls-client-required). |

## `ivicli api stop`

Stop the running Management API listener.

## `ivicli api token`

Manage Management API access tokens (ADR 0036).

## `ivicli api token create`

Mint a new API token. The raw token is printed once and never recoverable.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--label` |  | no | 1 | Human-readable label for the token (max 64 chars). |
| `--scope` |  | no | 0..100000 | Capability scope (repeatable). Allowed: read:devices / read:servers / read:scenarios / write:scpi. Omit for a legacy unrestricted token (ADR 0044). |
| `--expires` |  | no | 1 | Token expiry: a duration suffix (30d / 12h / 5m), or an ISO-8601 absolute instant. Omit for a token that never expires (ADR 0044). |

## `ivicli api token list`

List stored API tokens (hashes + metadata).

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit the listing as JSON instead of a table. |

## `ivicli api token revoke`

Remove the token with the supplied id.

| Argument | Arity | Description |
| --- | --- | --- |
| `id` | 1 | Token id (from `api token list`). |

## `ivicli doctor`

Report runtime, configuration, and backend-registration health.

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON. |

## `ivicli driver`

Inspect IVI drivers known to the local IVI Configuration Store.

## `ivicli driver list`

List installed IVI drivers (PRD §6.5).

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON instead of the human-readable table. |

## `ivicli logical`

Inspect IVI logical names known to the local IVI Configuration Store.

## `ivicli logical list`

List configured IVI logical names (PRD §6.5).

| Option | Aliases | Required | Arity | Description |
| --- | --- | --- | --- | --- |
| `--json` |  | no | 0..1 | Emit machine-readable JSON instead of the human-readable table. |

## `ivicli completion`

Emit a completion script for bash / zsh / PowerShell. Source it from your shell rc file.

| Argument | Arity | Description |
| --- | --- | --- |
| `shell` | 1 | Target shell: bash, zsh, or powershell. |
