# 0053. A device's timeout bounds every operation

- Status: Accepted
- Date: 2026-10-09

## Context

Every device carries `timeout_ms`, described by `visa add --timeout-ms`
and by the `Device` record as the per-device default operation timeout.
Until this ADR it bounded no I/O. The pool used it to bound the wait for
a device another session had leased (ADR 0038), and nothing else read
it:

- The Local backend opened every VISA session with 5 seconds and gave
  the session's I/O timeout the same 5 seconds.
- The HiSLIP, VXI-11 and SOCKET backends waited for a silent instrument
  until the caller cancelled; the VXI-11 client sent a fixed
  `io_timeout` of 5000.

The standards behind those transports say different things about
timeouts, and some say nothing. `docs/conformance.md` lists, with
section references, what each one specifies and what ivi-cli decides
where it is silent. This ADR records why the decisions are what they
are.

## Decision

### 1. One decorator bounds every backend

`DeviceTimeoutBackendFactory` wraps the backend factory outside the
plugin layer and inside the pool (`BackendLayers` in the CLI), so every
backend, built in or from a plugin, gets the same rule:

- write, query, read and trigger get the device timeout;
- a deadline that passes turns into `TransportTimeout`, while a
  cancellation from the caller propagates unchanged;
- the service-request stream is not bounded, since it waits for as long
  as the caller listens.

Bounding each backend separately would put the same deadline, the same
error mapping and the same recovery into five places, with SOCKET,
HiSLIP and VXI-11 already honouring cancellation and the serial
backends of #239 to come. One decorator keeps the rule in one place and
makes a new backend inherit it.

### 2. A backend that enforces the timeout itself gets a second of grace

A VXI-11 server enforces the `io_timeout` it is sent and answers error 15
when it runs out, and VXI-11 asks the client's own timeout to be longer
than `io_timeout`. A VISA runtime enforces `VI_ATTR_TMO_VALUE` the same
way. Such a backend implements `IEnforcesDeviceTimeout`, sends or sets
the device timeout, and the decorator waits one more second, so the
backend's own timeout error arrives first and the decorator is only the
backstop. One second covers a reply in flight on a local network without
making a silent instrument noticeably slower to fail.

The Local backend calls VISA synchronously, so for it the decorator cannot
step in at all: the runtime's own timeouts are the only bound on a VISA
call, and the decorator only adds the recovery below.

### 3. A session that timed out is closed and reopened

After a timeout, whether the decorator's deadline passed or the backend
returned `TransportTimeout` itself, the decorator closes the session and
reopens it before the next operation. A reply that arrives late would
otherwise be read as the answer to the next request on a transport with
no request identifiers, such as SOCKET or serial, and a VISA session that
timed out can still hold the reply in its buffers. VISA and VXI-11 leave
the session state after a timeout unspecified, and HiSLIP's MessageID
rules and device clear could resynchronise without reconnecting; one
rule for all transports was chosen over a recovery per transport. The
close gets the operation's limit too and is abandoned past it, because a
VXI-11 server answers calls in order and may still be busy with the call
that timed out; the VXI-11 backend releases its connection even when its close is
abandoned.

Closing the session ends its service-request stream. A gateway forwards
service requests for as long as a client's link lasts, so the
decorator's stream does not end with a session it dropped: it subscribes
to the reopened session's stream instead, and ends only when the caller
closes the session or stops listening.

Closing a HiSLIP connection releases any lock it held (IVI-6.1 §2.6), so
a timeout also gives up a HiSLIP lock.

### 4. Opening gets at least five seconds

Opening gets the longer of the device timeout and five seconds, plus the
grace of decision 2 for a backend that enforces the timeout itself. A short
I/O timeout, chosen so a query fails fast, is not meant to cut a network
connect short. VISA allows but does not require `viOpen`'s timeout to
bound opening a network resource, and recommends 2000 ms when it is used
with zero.

## Consequences

- `timeout_ms` now changes behaviour on every backend. A device with the
  default 3000 ms whose measurement takes longer than that used to succeed
  through HiSLIP or SOCKET, and through VXI-11 up to the fixed 5 seconds;
  it now times out and has to have its `timeout_ms` raised.
- A timeout costs a reconnect on the next operation, including one the
  VXI-11 server or the VISA runtime reports.
- Service requests raised while a dropped session is closed, before the
  next operation reopens it, are lost.
- The VXI-11 gateway still does not enforce a client's `io_timeout`
  (#249); `docs/conformance.md` lists it as a deviation.
