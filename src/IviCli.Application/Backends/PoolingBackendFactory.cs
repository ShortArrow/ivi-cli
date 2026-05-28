using System.Collections.Concurrent;
using IviCli.Application.Telemetry;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using Microsoft.Extensions.Logging;

namespace IviCli.Application.Backends;

/// <summary>
/// <see cref="IBackendFactory"/> decorator that caches per-device
/// <see cref="IIviBackend"/> sessions between caller open/close cycles
/// (ADR 0038). Cap is one in-flight op per device — VISA sessions are
/// not thread-safe per IVI spec, so concurrent lease attempts on the
/// same device serialise behind a <see cref="SemaphoreSlim"/>. Idle
/// entries close after <see cref="PoolConfig.IdleTimeout"/>;
/// <see cref="PoolConfig.MaxDevices"/> enforces an LRU upper bound.
/// </summary>
public sealed class PoolingBackendFactory : IBackendFactory, IAsyncDisposable
{
    private readonly IBackendFactory _inner;
    private readonly PoolConfig _config;
    private readonly TimeProvider _time;
    private readonly ILogger<PoolingBackendFactory>? _logger;
    private readonly ConcurrentDictionary<DeviceName, PoolEntry> _entries = new();
    private readonly ITimer? _sweepTimer;
    private readonly SemaphoreSlim _evictionGate = new(1, 1);
    private bool _disposed;

    /// <summary>Creates a pool wrapping <paramref name="inner"/>.</summary>
    public PoolingBackendFactory(
        IBackendFactory inner,
        PoolConfig config,
        TimeProvider time,
        ILogger<PoolingBackendFactory>? logger = null
    )
    {
        _inner = inner;
        _config = config;
        _time = time;
        _logger = logger;
        if (config.IdleTimeout > TimeSpan.Zero)
        {
            var sweepInterval = TimeSpan.FromMilliseconds(
                Math.Max(1000, config.IdleTimeout.TotalMilliseconds / 2)
            );
            _sweepTimer = _time.CreateTimer(
                _ => SweepIdle(),
                state: null,
                sweepInterval,
                sweepInterval
            );
        }
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device) =>
        Result.Success<IIviBackend, BackendError>(new PoolingBackendProxy(this, device));

    /// <summary>Test seam: number of currently-cached entries.</summary>
    public int CachedEntryCount => _entries.Count;

    internal async Task<Result<Lease, BackendError>> LeaseAsync(Device device, CancellationToken ct)
    {
        // Lazy eviction sweep on every lease attempt — keeps the pool
        // tidy even when no traffic flows long enough to trigger the
        // background timer.
        SweepIdle();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (!_entries.TryGetValue(device.Name, out var entry))
            {
                // Slow path: open a fresh backend. Serialise the open
                // attempt under _evictionGate so two callers requesting
                // the same brand-new device don't both call inner.OpenAsync.
                await _evictionGate.WaitAsync(ct);
                try
                {
                    if (!_entries.TryGetValue(device.Name, out entry))
                    {
                        EnforceMaxDevices();
                        var created = await OpenInnerAsync(device, ct);
                        if (created is not Result<PoolEntry, BackendError>.Ok createdOk)
                        {
                            return Result.Failure<Lease, BackendError>(
                                ((Result<PoolEntry, BackendError>.Error)created).Err
                            );
                        }
                        entry = createdOk.Value;
                        _entries[device.Name] = entry;
                    }
                }
                finally
                {
                    _evictionGate.Release();
                }
            }

            // Wait for the entry's semaphore up to device.Timeout. The
            // wait honours `ct` so cancellation always wins over timeout.
            bool acquired;
            try
            {
                acquired = await entry.Semaphore.WaitAsync(device.Timeout.Value, ct);
            }
            catch (ObjectDisposedException)
            {
                // Disposed between lookup and Wait (eviction path). Retry.
                continue;
            }
            if (!acquired)
            {
                IviCliTelemetry.PoolLeaseWaitTimeouts.Add(
                    1,
                    new KeyValuePair<string, object?>("ivi.device", device.Name.Value)
                );
                return Result.Failure<Lease, BackendError>(
                    new PoolWaitTimeout(device.Name, device.Timeout.Value)
                );
            }

            // Re-validate: the entry may have been evicted while we held
            // off waiting. Release the (now-orphaned) semaphore and retry.
            if (!_entries.TryGetValue(device.Name, out var current) || current != entry)
            {
                try
                {
                    entry.Semaphore.Release();
                }
                catch (ObjectDisposedException)
                { /* already evicted */
                }
                continue;
            }

            return Result.Success<Lease, BackendError>(new Lease(entry, this, device));
        }
    }

    internal void Release(Lease lease, bool broken)
    {
        var entry = lease.Entry;
        entry.LastUsed = _time.GetUtcNow();
        if (broken)
        {
            _logger?.LogDebug("pool: evicting broken entry for {Device}", lease.Device.Name.Value);
            EvictEntry(lease.Device.Name, entry);
        }
        try
        {
            entry.Semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // Evicted entry: semaphore already disposed.
        }
    }

    private void EnforceMaxDevices()
    {
        if (_config.MaxDevices <= 0)
        {
            return; // 0 = unlimited
        }
        while (_entries.Count >= _config.MaxDevices)
        {
            // Choose the LRU entry that is currently idle (semaphore free).
            (DeviceName Name, PoolEntry Entry)? victim = null;
            foreach (var kvp in _entries)
            {
                if (kvp.Value.Semaphore.CurrentCount == 0)
                {
                    // Currently leased; skip.
                    continue;
                }
                if (victim is null || kvp.Value.LastUsed < victim.Value.Entry.LastUsed)
                {
                    victim = (kvp.Key, kvp.Value);
                }
            }
            if (victim is null)
            {
                // Every entry is in use — nothing safe to evict. Allow
                // the pool to exceed MaxDevices temporarily; the next
                // sweep / release will reclaim.
                return;
            }
            EvictEntry(victim.Value.Name, victim.Value.Entry);
        }
    }

    private void EvictEntry(DeviceName name, PoolEntry entry)
    {
        if (_entries.TryRemove(new KeyValuePair<DeviceName, PoolEntry>(name, entry)))
        {
            IviCliTelemetry.PoolEvictions.Add(
                1,
                new KeyValuePair<string, object?>("ivi.device", name.Value)
            );
            _ = Task.Run(async () =>
            {
                try
                {
                    _ = await entry.Backend.CloseAsync(entry.Device, CancellationToken.None);
                }
                catch
                {
                    // Best-effort close on eviction — entry is gone either way.
                }
                finally
                {
                    entry.Dispose();
                }
            });
        }
    }

    private void SweepIdle()
    {
        if (_disposed)
        {
            return;
        }
        var now = _time.GetUtcNow();
        foreach (var kvp in _entries)
        {
            if (kvp.Value.Semaphore.CurrentCount == 0)
            {
                continue; // currently leased
            }
            if (now - kvp.Value.LastUsed > _config.IdleTimeout)
            {
                EvictEntry(kvp.Key, kvp.Value);
            }
        }
    }

    private async Task<Result<PoolEntry, BackendError>> OpenInnerAsync(
        Device device,
        CancellationToken ct
    )
    {
        var created = _inner.CreateFor(device);
        if (created is not Result<IIviBackend, BackendError>.Ok createdOk)
        {
            return Result.Failure<PoolEntry, BackendError>(
                ((Result<IIviBackend, BackendError>.Error)created).Err
            );
        }
        var opened = await createdOk.Value.OpenAsync(device, ct);
        if (opened is not Result<Unit, BackendError>.Ok)
        {
            return Result.Failure<PoolEntry, BackendError>(
                ((Result<Unit, BackendError>.Error)opened).Err
            );
        }
        return Result.Success<PoolEntry, BackendError>(
            new PoolEntry(createdOk.Value, device, _time.GetUtcNow())
        );
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_sweepTimer is IAsyncDisposable asyncTimer)
        {
            await asyncTimer.DisposeAsync();
        }
        else
        {
            _sweepTimer?.Dispose();
        }
        foreach (var kvp in _entries)
        {
            try
            {
                _ = await kvp.Value.Backend.CloseAsync(kvp.Value.Device, CancellationToken.None);
            }
            catch
            {
                // Best-effort.
            }
            finally
            {
                kvp.Value.Dispose();
            }
        }
        _entries.Clear();
        _evictionGate.Dispose();
    }

    internal sealed class PoolEntry : IDisposable
    {
        public PoolEntry(IIviBackend backend, Device device, DateTimeOffset openedAt)
        {
            Backend = backend;
            Device = device;
            LastUsed = openedAt;
            Semaphore = new SemaphoreSlim(1, 1);
        }

        public IIviBackend Backend { get; }
        public Device Device { get; }
        public SemaphoreSlim Semaphore { get; }
        public DateTimeOffset LastUsed { get; set; }

        public void Dispose() => Semaphore.Dispose();
    }

    internal sealed record Lease(PoolEntry Entry, PoolingBackendFactory Pool, Device Device);

    private sealed class PoolingBackendProxy : IIviBackend
    {
        private readonly PoolingBackendFactory _pool;
        private readonly Device _device;
        private Lease? _lease;
        private bool _broken;

        public PoolingBackendProxy(PoolingBackendFactory pool, Device device)
        {
            _pool = pool;
            _device = device;
        }

        public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
        {
            var leaseResult = await _pool.LeaseAsync(device, ct);
            if (leaseResult is not Result<Lease, BackendError>.Ok ok)
            {
                return Result.Failure<Unit, BackendError>(
                    ((Result<Lease, BackendError>.Error)leaseResult).Err
                );
            }
            _lease = ok.Value;
            _broken = false;
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
        {
            if (_lease is { } lease)
            {
                _pool.Release(lease, _broken);
                _lease = null;
            }
            return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
        }

        public async Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        )
        {
            if (_lease is not { } lease)
            {
                return Result.Failure<Unit, BackendError>(
                    new TransportDisconnected("backend not opened")
                );
            }
            var result = await lease.Entry.Backend.WriteAsync(device, command, ct);
            if (result is Result<Unit, BackendError>.Error)
            {
                _broken = true;
            }
            return result;
        }

        public async Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        )
        {
            if (_lease is not { } lease)
            {
                return Result.Failure<string, BackendError>(
                    new TransportDisconnected("backend not opened")
                );
            }
            var result = await lease.Entry.Backend.QueryAsync(device, query, ct);
            if (result is Result<string, BackendError>.Error)
            {
                _broken = true;
            }
            return result;
        }

        public async Task<Result<string, BackendError>> ReadAsync(
            Device device,
            CancellationToken ct
        )
        {
            if (_lease is not { } lease)
            {
                return Result.Failure<string, BackendError>(
                    new TransportDisconnected("backend not opened")
                );
            }
            var result = await lease.Entry.Backend.ReadAsync(device, ct);
            if (result is Result<string, BackendError>.Error)
            {
                _broken = true;
            }
            return result;
        }
    }
}
