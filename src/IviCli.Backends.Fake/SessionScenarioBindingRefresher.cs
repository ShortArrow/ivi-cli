using IviCli.Application.Backends;
using IviCli.Application.Mock;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;
using Microsoft.Extensions.Logging;

namespace IviCli.Backends.Fake;

/// <summary>
/// <see cref="IScenarioBindingRefresher"/> backed by the persisted
/// session and scenario stores. Reconciles the running
/// <see cref="FakeBackend"/>'s in-memory binding for a device against the
/// session's <c>DeviceScenarios</c>, re-applying only when the bound
/// scenario name changed so scene / transition state is preserved for
/// unchanged bindings.
/// </summary>
public sealed class SessionScenarioBindingRefresher : IScenarioBindingRefresher
{
    private readonly FakeBackend _fake;
    private readonly IScenarioStore _scenarios;
    private readonly ISessionStore _sessions;
    private readonly ILogger<SessionScenarioBindingRefresher> _logger;

    /// <summary>Creates a refresher over the supplied backend and stores.</summary>
    public SessionScenarioBindingRefresher(
        FakeBackend fake,
        IScenarioStore scenarios,
        ISessionStore sessions,
        ILogger<SessionScenarioBindingRefresher> logger
    )
    {
        _fake = fake;
        _scenarios = scenarios;
        _sessions = sessions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(Device device, CancellationToken ct)
    {
        if (
            await _sessions.LoadAsync(ct)
            is not Result<SessionState, SessionStoreError>.Ok { Value: var session }
        )
        {
            return;
        }

        var desired = session.GetActiveScenario(device.Name);
        var current = _fake.GetActiveScenario(device.Name)?.Name;

        if (desired is null)
        {
            if (current is not null)
            {
                _fake.DeactivateScenario(device.Name);
            }
            return;
        }

        if (desired == current)
        {
            return;
        }

        if (
            await _scenarios.LoadAsync(desired, ct) is Result<MockScenario, ScenarioStoreError>.Ok
            {
                Value: var scenario
            }
        )
        {
            _fake.ActivateScenario(scenario, device.Name);
        }
        else
        {
            _logger.LogWarning(
                "could not load scenario {Name} while refreshing binding for device {Device}: keeping current binding",
                desired.Value,
                device.Name.Value
            );
        }
    }
}
