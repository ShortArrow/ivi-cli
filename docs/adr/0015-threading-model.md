# 0015. Threading Model

- Status: Accepted
- Date: 2026-05-23

## Context

ADR 0023 §7 already declared the async baseline: `async`/`await`,
mandatory `CancellationToken` on every public async method, no
`ConfigureAwait(false)` in CLI binaries, no `.Result` / `.Wait()`,
no `async void`. That rule covers the one-shot CLI invocation cleanly.

Phase 2 introduces a *long-lived* process — the gateway server.
A `TcpListener` accept loop, per-connection tasks, and a cooperative
shutdown trigger via the CLI all need explicit rules so the
implementation does not drift into ad-hoc fire-and-forget tasks or
deadlock-prone synchronization primitives.

This ADR records the threading discipline for both the existing CLI
process and the new gateway-server process.

## Decision

### 1. Async by default

- All I/O paths are `async`. Adapters wrap any synchronous library
  call (e.g. NI-VISA SDK) inside `Task.Run(...)` only when the
  underlying API has no async variant; the wrap is the adapter's
  problem and never leaks to the Application layer.
- Domain code is synchronous and pure (no `async`); ADR 0023 §6.

### 2. CancellationToken propagation

- Every public async method declared in Application / Infrastructure
  takes a `CancellationToken` argument with no default value
  (ADR 0023 §7).
- The gateway server propagates a single root token from
  `IGatewayServer.StopAsync` through every connection task, all the
  way down to `IIviBackend.QueryAsync` calls. No task may "decide
  on its own" to ignore cancellation.

### 3. Listener accept loop

- One async loop per protocol instance:

  ```csharp
  while (!ct.IsCancellationRequested)
  {
      var client = await listener.AcceptTcpClientAsync(ct);
      _ = HandleConnectionAsync(client, ct); // fire-and-forget per connection
  }
  ```

- The fire-and-forget pattern is permitted *only* here. The handler
  itself wraps everything in `try` / `catch (OperationCanceledException)`
  / `catch (Exception)` so a single bad connection never tears down
  the listener.
- The handler is logged via the connection's `ILogger` scope, with
  remote endpoint and route name (ADR 0011 §7 / ADR 0007 §10).

### 4. Per-connection state

- Each connection task owns its `NetworkStream` and any per-session
  protocol state (e.g. HiSLIP session ID, max message size).
- Cross-connection state lives on the gateway-server instance and is
  protected by either immutability or a single `ConcurrentDictionary`
  / `Channel<T>` — no `lock(this)`, no `Monitor.Enter` on shared
  mutable state, no `Mutex`.
- Per-device backend access is single-threaded *per connection* via
  the linear request/response model of HiSLIP and SOCKET. A future
  device-level lock can be added when locking semantics ship; v1 does
  not need one.

### 5. Graceful shutdown

- `IGatewayServer.StopAsync` cancels the root token, awaits the accept
  loop's completion (max 5 seconds), then closes the listener.
- Connection tasks observe the token at each `await` and the next
  read/write boundary, then close their socket via `using` / `await
  using`.
- Hard shutdown (token fires + 5-second grace expires) is a last
  resort and is logged at Warning.

### 6. No `ConfigureAwait(false)` (carry-forward from 0023 §7)

CLI binaries and gateway servers both run without a custom
`SynchronizationContext`, so `ConfigureAwait(false)` is noise. ADR
0023 §7 forbids it; this ADR reaffirms.

### 7. No `Task.Wait()` / `Task.Result` / `GetAwaiter().GetResult()`

These block a thread waiting on a Task and risk deadlocks in any
future hosted-service or sync context. They are prohibited in all
production code. Tests may use `await Should.ThrowAsync(...)` instead.

### 8. `async void` rule

- `async void` is permitted only for `EventHandler`-shaped methods
  (the CLR signature requires it). Everywhere else, `async Task` or
  `async Task<T>`.
- The `Console.CancelKeyPress` handler is the one place this exception
  applies in the codebase.

### 9. Thread-pool sizing

- No explicit `ThreadPool.SetMinThreads` calls in v1. The default
  growth heuristic is adequate for the expected per-host connection
  count.
- A follow-up ADR can revisit if benchmark data shows pathological
  starvation.

### 10. Synchronization primitives policy

Permitted in the codebase:

- `CancellationTokenSource` / `CancellationToken`.
- `ConcurrentDictionary<TKey,TValue>` / `Channel<T>` for cross-task
  state.
- `SemaphoreSlim` when a bounded resource is genuinely shared (none in
  v1; documented if introduced later).

Prohibited unless an ADR amendment opts in:

- `lock`-on-`this` / `lock`-on-public-field.
- `Monitor.Enter` outside of language-`lock` blocks.
- `Thread.Sleep` in production code (`Task.Delay(..., ct)` instead).
- `BlockingCollection`, `EventWaitHandle`, raw `Mutex`.

## Consequences

**Pros**

- A single set of rules covers the CLI one-shot and the gateway
  long-lived process, so reviewers do not need to context-switch.
- Graceful shutdown is a deterministic protocol (token → accept loop
  exits → connections drain → listener closes), avoiding
  hard-to-test race conditions.
- The "no `.Result` / no `lock(this)`" stance keeps deadlock surfaces
  small for the asynchronous I/O the gateway must perform reliably.

**Cons**

- The per-connection fire-and-forget exception in §3 requires careful
  exception logging — a swallowed exception there can mask real bugs.
- Prohibiting `SemaphoreSlim`-style primitives except in the listed
  cases creates a small learning curve for contributors used to them.

**Mitigations**

- §3's handler must `catch (Exception)` and log at Error before
  closing the connection. A unit-test harness (TestKit) lands when
  the gateway server lands and asserts this.
- The "permitted / prohibited" lists in §10 are kept short and easy to
  refer to; deviations go through an ADR amendment so the trade-off
  is visible.
