**English** | [日本語](conformance.jp.md)

# Standards conformance

How ivi-cli relates to the standards it implements, area by area. Each
entry is one of three kinds:

- **Follows**: the standard specifies the behaviour, and ivi-cli does what
  it says. The entry cites the section.
- **Deviates**: the standard specifies the behaviour, and ivi-cli does
  something else. The entry links the issue that tracks it.
- **ivi-cli defines**: the standard is silent, so ivi-cli chose. The entry
  links the ADR that records why.

Every entry names the tests that pin it. A behaviour that no test pins
does not belong here until one does.

> This file is a living document: it describes the current behaviour and
> changes in the same pull request as the code. Its Japanese mirror,
> `conformance.jp.md`, changes with it.

Sources:

- VISA: IVI Foundation VPP-4.3, *The VISA Library*, Rev 7.2.1 (2024-01-04).
- HiSLIP: IVI Foundation IVI-6.1, *High-Speed LAN Instrument Protocol*,
  Rev 2.0 (2020-04-23).
- VXI-11: VXIbus Consortium, *TCP/IP Instrument Protocol Specification*,
  Rev 1.0 (1995-07-17).

## Device timeouts

A device's `timeout_ms` is the time one operation may take. Why each rule
below was chosen is in [ADR 0053](adr/0053-device-timeout.md).

### Every backend

| Kind | Behaviour | Tests |
| --- | --- | --- |
| ivi-cli defines | Write, query, read and trigger fail with `TransportTimeout` once `timeout_ms` has passed. This holds for every backend, built in or from a plugin, with or without the session pool. | [DeviceTimeoutBackendFactoryTests][dt], [ClientTimeoutTests][ct], [BackendLayersTests][bl] |
| ivi-cli defines | After a timeout, whether ivi-cli's deadline passed or the backend reported the timeout itself, the session is closed and reopened before the next operation, so a late reply is never read as the answer to a later request. Closing is abandoned if it takes longer than the operation was allowed. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | Opening a session may take the longer of `timeout_ms` and 5 seconds, plus the grace below for a backend that enforces the timeout itself. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | A backend that enforces the timeout itself (VXI-11, VISA) gets one second beyond `timeout_ms` before ivi-cli abandons the operation, so its own timeout error arrives first. | [DeviceTimeoutBackendFactoryTests][dt], [Vxi11BackendTests][vt], [LocalBackendTimeoutTests][lt] |
| ivi-cli defines | A cancellation from the caller is reported as a cancellation, not as a timeout. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | The service-request stream has no deadline. It carries on across a session that was reopened after a timeout, and ends when the session is closed by its owner: the caller, or the session pool when the pool is enabled. | [DeviceTimeoutBackendFactoryTests][dt] |

ivi-cli abandons an operation by cancelling it, so a backend that does not
honour cancellation is not cut short: the Local backend (below), or a
plugin backend that ignores its cancellation token.

### VISA runtime (Local backend)

| Kind | Behaviour | Tests |
| --- | --- | --- |
| Follows | The session's `VI_ATTR_TMO_VALUE`, the minimum time an operation waits in milliseconds, is `timeout_ms` (VPP-4.3 §5.1.2). | [LocalBackendTimeoutTests][lt] |
| Follows | A timeout the runtime reports, `VI_ERROR_TMO` from `viRead` or `viWrite` (VPP-4.3 §6.1.1, §6.1.4), is reported as `TransportTimeout`. A trigger is sent as `*TRG` through `viWrite`. | [VisaIoErrorsTests][ve], [LocalBackendTimeoutTests][lt] |
| ivi-cli defines | ivi-cli passes the longer of `timeout_ms` and 5 seconds as `viOpen`'s timeout. VPP-4.3 uses that timeout for acquiring a lock (§4.3.3.2, RULE 4.3.18) and lets a VISA implementation use it to bound opening the session too (PERMISSION 4.3.2), without requiring it, so whether opening is bounded depends on the installed runtime. | [LocalBackendTimeoutTests][lt] |
| ivi-cli defines | After `VI_ERROR_TMO` the session is reopened (above). VPP-4.3 says nothing about the session's state after a timeout and does not ask for a device clear. | [DeviceTimeoutBackendFactoryTests][dt] |

The Local backend calls the VISA runtime synchronously, so ivi-cli cannot
abandon a VISA call in progress: only the runtime's own timeouts end one.

### VXI-11 client

| Kind | Behaviour | Tests |
| --- | --- | --- |
| Follows | `timeout_ms` is sent as the `io_timeout` (B.5.4) of `device_write`, `device_read` and `device_trigger`, which the server enforces (RULE B.6.19, B.6.27, B.6.44). | [Vxi11BackendTests][vt] |
| Follows | The client has a timeout of its own, used when the server does not answer (RULE B.4.4). | [DeviceTimeoutBackendFactoryTests][dt], [Vxi11BackendTests][vt] |
| ivi-cli defines | The client's own timeout covers a whole operation: `io_timeout` plus one second, with `lock_timeout` sent as 0. VXI-11 suggests a client timeout longer than `io_timeout` plus `lock_timeout` for each call (OBSERVATION B.4.6). A query is a `device_write` followed by one or more `device_read` calls, so a later call can be cut short before its own `io_timeout` has passed. | [DeviceTimeoutBackendFactoryTests][dt], [Vxi11BackendTests][vt] |
| Follows | Error 15 from the server is reported as `TransportTimeout` (RULE B.6.19, B.6.27, B.6.44). | [ClientTimeoutTests][ct] |
| ivi-cli defines | After a timeout the link is torn down and created again before the next operation. VXI-11 does not say what state a link is in after error 15; it expects a client to discard a reply that arrives late (OBSERVATION B.4.7), which a new link guarantees. | [DeviceTimeoutBackendFactoryTests][dt] |

### VXI-11 gateway

| Kind | Behaviour | Tests |
| --- | --- | --- |
| Follows | An instrument behind the gateway that times out on a write is reported to the client as error 15 (RULE B.6.19). | [ClientTimeoutTests][ct] |
| Deviates | The gateway does not enforce the `io_timeout` a client sends; VXI-11 requires the server to end a call that runs past it with error 15 (RULE B.6.19, B.6.27, B.6.44). Tracked in [#249](https://github.com/ShortArrow/ivi-cli/issues/249). | — |

### HiSLIP client

| Kind | Behaviour | Tests |
| --- | --- | --- |
| ivi-cli defines | `timeout_ms` bounds each operation. IVI-6.1 has no client I/O timeout; its only timed limit is the lock timeout (§2.6, §6.5). | [ClientTimeoutTests][ct] |
| ivi-cli defines | After a timeout the connection is closed and opened again before the next operation, instead of resynchronising with a device clear (§6.12). Closing releases any lock the connection held (§2.6). | [DeviceTimeoutBackendFactoryTests][dt] |

### SOCKET client

| Kind | Behaviour | Tests |
| --- | --- | --- |
| ivi-cli defines | No standard covers a raw SOCKET connection. `timeout_ms` bounds each operation, and the connection is reopened after a timeout. | [ClientTimeoutTests][ct] |

[dt]: ../tests/IviCli.Application.Tests/Backends/DeviceTimeoutBackendFactoryTests.cs
[ct]: ../tests/IviCli.Server.Tests/ClientTimeoutTests.cs
[bl]: ../tests/IviCli.Cli.Tests/BackendLayersTests.cs
[lt]: ../tests/IviCli.Backends.Local.Tests/LocalBackendTests.cs
[ve]: ../tests/IviCli.Backends.Local.Tests/VisaIoErrorsTests.cs
[vt]: ../tests/IviCli.Backends.Vxi11.Tests/Vxi11BackendTests.cs
