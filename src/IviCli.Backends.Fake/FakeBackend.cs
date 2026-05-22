using System.Collections.Concurrent;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Backends.Fake;

/// <summary>
/// In-memory <see cref="IIviBackend"/> for tests and the local default
/// configuration. Implements the fault-injection surface declared in
/// ADR 0009 §6 so handler / Cli tests can describe instrument behavior
/// without touching real hardware.
/// </summary>
public sealed class FakeBackend : IIviBackend
{
    private readonly ConcurrentDictionary<DeviceName, FakeDeviceState> _devices = new();

    /// <summary>Configures the default IDN response for <paramref name="name"/>.</summary>
    public FakeBackend ConfigureDevice(DeviceName name, string idn)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.IdnResponse = idn;
        return this;
    }

    /// <summary>
    /// Arranges that the next <see cref="OpenAsync"/> for <paramref name="name"/>
    /// returns the supplied failure.
    /// </summary>
    public FakeBackend FailNextOpen(DeviceName name, BackendError failure)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.OpenFailure = failure;
        return this;
    }

    /// <summary>
    /// Arranges that any query matching <paramref name="scpiText"/> for
    /// <paramref name="name"/> returns <paramref name="response"/>.
    /// </summary>
    public FakeBackend RespondToQuery(DeviceName name, string scpiText, string response)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.QueryResponses[scpiText] = response;
        return this;
    }

    /// <summary>
    /// Arranges that any query matching <paramref name="scpiText"/> for
    /// <paramref name="name"/> fails with <paramref name="failure"/>.
    /// </summary>
    public FakeBackend FailQuery(DeviceName name, string scpiText, BackendError failure)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.QueryFailures[scpiText] = failure;
        return this;
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = _devices.GetOrAdd(device.Name, _ => new FakeDeviceState());
        if (state.OpenFailure is { } failure)
        {
            state.OpenFailure = null;
            return Task.FromResult(Result.Failure<Unit, BackendError>(failure));
        }
        state.IsOpen = true;
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_devices.TryGetValue(device.Name, out var state))
        {
            state.IsOpen = false;
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var state = _devices.GetOrAdd(device.Name, _ => new FakeDeviceState());
        state.LastWritten = command.Value;
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var state = _devices.GetOrAdd(device.Name, _ => new FakeDeviceState());

        if (state.QueryFailures.TryGetValue(query.Value, out var failure))
        {
            state.QueryFailures.Remove(query.Value);
            return Task.FromResult(Result.Failure<string, BackendError>(failure));
        }

        if (state.QueryResponses.TryGetValue(query.Value, out var response))
        {
            return Task.FromResult(Result.Success<string, BackendError>(response));
        }

        // Special-case the universal IDN query.
        if (query.Value.Equals("*IDN?", StringComparison.Ordinal))
        {
            var idn = state.IdnResponse ?? "FAKE,FAKE,0,1.0";
            return Task.FromResult(Result.Success<string, BackendError>(idn));
        }

        // Default echo for unprogrammed queries.
        return Task.FromResult(Result.Success<string, BackendError>(query.Value));
    }

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = _devices.GetOrAdd(device.Name, _ => new FakeDeviceState());
        return Task.FromResult(
            Result.Success<string, BackendError>(state.LastWritten ?? string.Empty)
        );
    }

    private sealed class FakeDeviceState
    {
        public string? IdnResponse { get; set; }
        public string? LastWritten { get; set; }
        public bool IsOpen { get; set; }
        public BackendError? OpenFailure { get; set; }
        public Dictionary<string, string> QueryResponses { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BackendError> QueryFailures { get; } =
            new(StringComparer.Ordinal);
    }
}
