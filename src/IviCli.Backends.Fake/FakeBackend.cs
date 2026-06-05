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
    private readonly ConcurrentDictionary<DeviceName, ActiveBinding> _bindings = new();
    private readonly object _sceneGate = new();

    /// <inheritdoc/>
    public bool HasActiveScenario => !_bindings.IsEmpty;

    /// <inheritdoc/>
    public bool HasActiveScenarioFor(Device device) => _bindings.ContainsKey(device.Name);

    /// <summary>Configures the default IDN response for <paramref name="name"/>.</summary>
    public FakeBackend ConfigureDevice(DeviceName name, string idn)
    {
        var state = _devices.GetOrAdd(name, _ => new FakeDeviceState());
        state.IdnResponse = idn;
        return this;
    }

    /// <summary>
    /// Activates a mock scenario for <paramref name="device"/> only.
    /// Resets that device's current scene to the scenario's
    /// <see cref="MockScenario.InitialScene"/>; matching rules whose
    /// action carries a <c>Transition</c> move the FakeBackend to a
    /// different scene at runtime (issue #26 §"Implementation plan" —
    /// B0.2-3). Different devices may have different scenarios
    /// active simultaneously (issue #36 / v0.2.4). The scenario's
    /// rules take precedence over the programmatic DSL
    /// (<see cref="RespondToQuery"/>, etc.) when a match is found;
    /// otherwise the existing fallthrough applies (ADR 0026 §4).
    /// </summary>
    public FakeBackend ActivateScenario(MockScenario scenario, DeviceName device)
    {
        lock (_sceneGate)
        {
            _bindings[device] = new ActiveBinding(scenario, scenario.InitialScene);
        }
        return this;
    }

    /// <summary>Removes the active scenario binding for <paramref name="device"/>.</summary>
    public FakeBackend DeactivateScenario(DeviceName device)
    {
        lock (_sceneGate)
        {
            _bindings.TryRemove(device, out _);
        }
        return this;
    }

    /// <summary>Removes every active scenario binding.</summary>
    public FakeBackend DeactivateAllScenarios()
    {
        lock (_sceneGate)
        {
            _bindings.Clear();
        }
        return this;
    }

    /// <summary>Returns the scenario currently bound to <paramref name="device"/>, or null.</summary>
    public MockScenario? GetActiveScenario(DeviceName device) =>
        _bindings.TryGetValue(device, out var b) ? b.Scenario : null;

    /// <summary>Returns the current scene for <paramref name="device"/>, or null.</summary>
    public SceneName? GetCurrentScene(DeviceName device) =>
        _bindings.TryGetValue(device, out var b) ? b.CurrentScene : null;

    /// <summary>
    /// Looks up a rule in the active scenario's current scene for
    /// <paramref name="device"/>, or <see langword="null"/> when no
    /// binding exists or no rule matches.
    /// </summary>
    private MockRule? FindRuleInCurrentScene(DeviceName device, string scpi)
    {
        if (!_bindings.TryGetValue(device, out var binding))
        {
            return null;
        }
        return binding.Scenario.FindScene(binding.CurrentScene)?.FindByMatch(scpi);
    }

    /// <summary>
    /// Applies a rule action's optional transition for
    /// <paramref name="device"/>. Silently ignored when the target
    /// scene does not exist in the bound scenario.
    /// </summary>
    private void ApplyTransition(DeviceName device, SceneName? target)
    {
        if (target is null)
        {
            return;
        }
        lock (_sceneGate)
        {
            if (
                _bindings.TryGetValue(device, out var binding)
                && binding.Scenario.FindScene(target) is not null
            )
            {
                _bindings[device] = binding with { CurrentScene = target };
            }
        }
    }

    private sealed record ActiveBinding(MockScenario Scenario, SceneName CurrentScene);

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

        // Scenario takes precedence over the programmatic DSL. Look up
        // the rule in the active scenario's *current* scene (B0.2-3),
        // not the flat rule list — once Transition actions land, the
        // current scene moves at runtime so the same SCPI string may
        // match different rules at different points in the session.
        var rule = FindRuleInCurrentScene(device.Name, command.Value);
        if (rule is not null)
        {
            // Effect first, then transition: a Fail rule reports its
            // canned failure before moving the scene; an Ack
            // succeeds first, then moves on.
            var effect = rule.Action switch
            {
                RuleAction.Ack => Result.Success<Unit, BackendError>(Unit.Value),
                RuleAction.Fail f => Result.Failure<Unit, BackendError>(
                    BuildFailure(command.Value, f)
                ),
                RuleAction.Respond => Result.Failure<Unit, BackendError>(
                    new MockScenarioContractMismatch(
                        command.Value,
                        "scenario rule has `respond` but WriteAsync expects `ack`"
                    )
                ),
                _ => Result.Success<Unit, BackendError>(Unit.Value),
            };
            ApplyTransition(device.Name, rule.Action.Transition);
            return Task.FromResult(effect);
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

        // Scenario takes precedence. Look up the rule in the active
        // scenario's *current* scene (B0.2-3); same rationale as the
        // WriteAsync path above.
        var rule = FindRuleInCurrentScene(device.Name, query.Value);
        if (rule is not null)
        {
            var effect = rule.Action switch
            {
                RuleAction.Respond r => Result.Success<string, BackendError>(r.Text),
                RuleAction.Fail f => Result.Failure<string, BackendError>(
                    BuildFailure(query.Value, f)
                ),
                RuleAction.Ack => Result.Failure<string, BackendError>(
                    new MockScenarioContractMismatch(
                        query.Value,
                        "scenario rule has `ack` but QueryAsync expects `respond`"
                    )
                ),
                _ => Result.Success<string, BackendError>(query.Value),
            };
            ApplyTransition(device.Name, rule.Action.Transition);
            return Task.FromResult(effect);
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
            var idn =
                GetActiveScenario(device.Name)?.IdnDefault
                ?? state.IdnResponse
                ?? "FAKE,FAKE,0,1.0";
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

    private static BackendError BuildFailure(string match, RuleAction.Fail fail) =>
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
