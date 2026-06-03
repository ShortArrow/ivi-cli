using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;

namespace IviCli.Backends.Fake;

/// <summary>
/// In-memory <see cref="IIviBackend"/> for tests and the local default
/// configuration. Implements the fault-injection surface declared in
/// ADR 0009 §6 and the scenario-playback hook declared in ADR 0026 §4.
/// </summary>
public sealed class FakeBackend : IIviBackend, IScenarioAwareBackend
{
    private readonly ConcurrentDictionary<DeviceName, FakeDeviceState> _devices = new();
    private MockScenario? _activeScenario;

    /// <inheritdoc/>
    public bool HasActiveScenario => _activeScenario is not null;

    /// <summary>Configures the default IDN response for <paramref name="name"/>.</summary>
    public FakeBackend ConfigureDevice(DeviceName name, string idn)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.IdnResponse = idn;
        return this;
    }

    /// <summary>
    /// Activates a mock scenario. The scenario's scenes take precedence over
    /// the programmatic DSL (<see cref="RespondToQuery"/>, etc.) when a match
    /// is found; otherwise the existing fallthrough applies (ADR 0026 §4).
    /// </summary>
    public FakeBackend ActivateScenario(MockScenario scenario)
    {
        _activeScenario = scenario;
        return this;
    }

    /// <summary>Removes any active scenario.</summary>
    public FakeBackend DeactivateScenario()
    {
        _activeScenario = null;
        return this;
    }

    /// <summary>The currently activated scenario, if any.</summary>
    public MockScenario? ActiveScenario => _activeScenario;

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
        state.OpenCount++;
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_devices.TryGetValue(device.Name, out var state))
        {
            state.IsOpen = false;
            state.CloseCount++;
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <summary>Number of times <see cref="OpenAsync"/> was called for <paramref name="name"/>.</summary>
    public int OpenCountFor(DeviceName name) =>
        _devices.TryGetValue(name, out var state) ? state.OpenCount : 0;

    /// <summary>Number of times <see cref="CloseAsync"/> was called for <paramref name="name"/>.</summary>
    public int CloseCountFor(DeviceName name) =>
        _devices.TryGetValue(name, out var state) ? state.CloseCount : 0;

    /// <summary>Number of times <see cref="TriggerAsync"/> was called for <paramref name="name"/>.</summary>
    public int TriggerCountFor(DeviceName name) =>
        _devices.TryGetValue(name, out var state) ? state.TriggerCount : 0;

    /// <summary>
    /// Pushes a synthetic Service Request onto the per-device SRQ channel
    /// so any <see cref="ServiceRequestStream"/> consumer observes it
    /// (test affordance — production code does not call this).
    /// </summary>
    public void RaiseServiceRequest(DeviceName name, byte statusByte = 0x40)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.ServiceRequestChannel.Writer.TryWrite(
            new ServiceRequest(name, statusByte, DateTimeOffset.UtcNow)
        );
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

        // Scenario takes precedence over the programmatic DSL.
        if (_activeScenario?.FindByMatch(command.Value) is { } scene)
        {
            return scene.Action switch
            {
                SceneAction.Ack => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value)),
                SceneAction.Fail f => Task.FromResult(
                    Result.Failure<Unit, BackendError>(BuildFailure(command.Value, f))
                ),
                SceneAction.Respond => Task.FromResult(
                    Result.Failure<Unit, BackendError>(
                        new MockScenarioContractMismatch(
                            command.Value,
                            "scenario scene has `respond` but WriteAsync expects `ack`"
                        )
                    )
                ),
                _ => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value)),
            };
        }

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

        // Scenario takes precedence.
        if (_activeScenario?.FindByMatch(query.Value) is { } scene)
        {
            return scene.Action switch
            {
                SceneAction.Respond r => Task.FromResult(
                    Result.Success<string, BackendError>(r.Text)
                ),
                SceneAction.Fail f => Task.FromResult(
                    Result.Failure<string, BackendError>(BuildFailure(query.Value, f))
                ),
                SceneAction.Ack => Task.FromResult(
                    Result.Failure<string, BackendError>(
                        new MockScenarioContractMismatch(
                            query.Value,
                            "scenario scene has `ack` but QueryAsync expects `respond`"
                        )
                    )
                ),
                _ => Task.FromResult(Result.Success<string, BackendError>(query.Value)),
            };
        }

        if (state.QueryFailures.TryGetValue(query.Value, out var failure))
        {
            state.QueryFailures.Remove(query.Value);
            return Task.FromResult(Result.Failure<string, BackendError>(failure));
        }

        if (state.QueryResponses.TryGetValue(query.Value, out var response))
        {
            return Task.FromResult(Result.Success<string, BackendError>(response));
        }

        // Universal *IDN? fallback: prefer the active scenario's IdnDefault,
        // then the device-configured IDN, then a generic FAKE response.
        if (query.Value.Equals("*IDN?", StringComparison.Ordinal))
        {
            var idn = _activeScenario?.IdnDefault ?? state.IdnResponse ?? "FAKE,FAKE,0,1.0";
            return Task.FromResult(Result.Success<string, BackendError>(idn));
        }

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

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var state = _devices.GetOrAdd(device.Name, _ => new FakeDeviceState());
        state.TriggerCount++;
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var state = _devices.GetOrAdd(device.Name, _ => new FakeDeviceState());
        var reader = state.ServiceRequestChannel.Reader;
        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var srq))
            {
                yield return srq;
            }
        }
    }

    private static BackendError BuildFailure(string match, SceneAction.Fail fail) =>
        fail.Variant.ToLowerInvariant() switch
        {
            "transport_timeout" => new TransportTimeout(ParseTimeSpan(fail.Detail)),
            "transport_disconnected" => new TransportDisconnected(
                fail.Detail ?? "scenario-injected disconnect"
            ),
            _ => new MockScenarioContractMismatch(match, $"unknown fail variant: {fail.Variant}"),
        };

    private static TimeSpan ParseTimeSpan(string? detail)
    {
        if (
            detail is not null
            && int.TryParse(detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            && ms >= 0
        )
        {
            return TimeSpan.FromMilliseconds(ms);
        }
        return TimeSpan.Zero;
    }

    private sealed class FakeDeviceState
    {
        public string? IdnResponse { get; set; }
        public string? LastWritten { get; set; }
        public bool IsOpen { get; set; }
        public int OpenCount { get; set; }
        public int CloseCount { get; set; }
        public int TriggerCount { get; set; }
        public BackendError? OpenFailure { get; set; }
        public Dictionary<string, string> QueryResponses { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BackendError> QueryFailures { get; } =
            new(StringComparer.Ordinal);
        public Channel<ServiceRequest> ServiceRequestChannel { get; } =
            Channel.CreateUnbounded<ServiceRequest>();
    }
}
