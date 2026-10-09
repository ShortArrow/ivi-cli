# Standards conformance

English | [日本語](conformance.jp.md)

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
| ivi-cli defines | Write, query, read and trigger fail with `TransportTimeout` once `timeout_ms` has passed. | [DeviceTimeoutBackendFactoryTests][dt], [ClientTimeoutTests][ct] |
| ivi-cli defines | After a timeout the session is closed, and reopened before the next operation, so a late reply is never read as the answer to a later request. Closing is abandoned if it takes longer than the operation was allowed. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | Opening a session may take the longer of `timeout_ms` and 5 seconds. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | A backend that enforces the timeout itself (VXI-11, VISA) gets one second beyond `timeout_ms` before ivi-cli steps in, so its own timeout error arrives first. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | A cancellation from the caller is reported as a cancellation, not as a timeout. The service-request stream has no deadline. | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli defines | The default `timeout_ms` for `visa add` is 3000 ms. VISA's default for `VI_ATTR_TMO_VALUE` is 2000 ms (VPP-4.3, Appendix B). | — |

### VISA runtime (Local backend)

| Kind | Behaviour | Tests |
| --- | --- | --- |
| Follows | The session's `VI_ATTR_TMO_VALUE`, the minimum time an operation waits in milliseconds, is `timeout_ms` (VPP-4.3 §5.1.2). | [LocalBackendTimeoutTests][lt] |
| Follows | `VI_ERROR_TMO` from the runtime is reported as `TransportTimeout` (VPP-4.3 §6.1.1). | [LocalBackendTimeoutTests][lt] |
| ivi-cli defines | `viOpen`'s timeout bounds opening the session as well as acquiring a lock, which VPP-4.3 permits (§4.3.3.2, PERMISSION 4.3.2) without requiring it. | [LocalBackendTimeoutTests][lt] |
| ivi-cli defines | After `VI_ERROR_TMO` the session is reopened (above). VPP-4.3 says nothing about the session's state after a timeout and does not ask for a device clear. | [DeviceTimeoutBackendFactoryTests][dt] |

### VXI-11 client

| Kind | Behaviour | Tests |
| --- | --- | --- |
| Follows | `timeout_ms` is sent as the `io_timeout` of `device_write`, `device_read` and `device_trigger`, which the server enforces (VXI-11 B.4.3, B.5.4). | [ClientTimeoutTests][ct] |
| Follows | The client's own deadline is one second longer than `io_timeout`; VXI-11 asks for a client timeout greater than `io_timeout` plus `lock_timeout` (B.4.3). | [DeviceTimeoutBackendFactoryTests][dt] |
| Follows | Error 15 from the server is reported as `TransportTimeout` (B.6.3, B.6.4). | [ClientTimeoutTests][ct] |
| ivi-cli defines | After a timeout the link is torn down and created again before the next operation. VXI-11 does not say what state a link is in after error 15. | [DeviceTimeoutBackendFactoryTests][dt] |

### VXI-11 gateway

| Kind | Behaviour | Tests |
| --- | --- | --- |
| Follows | An instrument behind the gateway that times out is reported to the client as error 15 (B.6.3). | [ClientTimeoutTests][ct] |
| Deviates | The gateway does not enforce the `io_timeout` a client sends; VXI-11 requires the server to end a call that runs past it with error 15 (B.4.3, B.6.3, B.6.4). Tracked in [#249](https://github.com/ShortArrow/ivi-cli/issues/249). | — |

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
[lt]: ../tests/IviCli.Backends.Local.Tests/LocalBackendTests.cs
