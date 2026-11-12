using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;

namespace IviCli.Application.Mock;

/// <summary>
/// Application-layer handler for <c>mock unserve</c>: removes the
/// <c>MOCK::INSTR</c> device with the name and its binding, and the server
/// with the name with its route (ADR 0056 §7).
/// </summary>
/// <remarks>
/// Nothing is removed unless the device's resource is <c>MOCK::INSTR</c>,
/// no other server routes to it, the server, if there is one, has one of
/// the four mock types and at most the one route <c>mock serve</c> gives it,
/// and no process recorded under the name runs. A record whose process is
/// gone is removed with the entries. The scenario is kept.
/// </remarks>
public sealed class UnserveMockCommandHandler
{
    private readonly IConfigStore _config;
    private readonly ISessionStore _session;
    private readonly IServerProcessRegistry _registry;
    private readonly IProcessLiveness _liveness;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates a new handler. <paramref name="audit"/> and
    /// <paramref name="subject"/> record one <see cref="ConfigMutated"/>,
    /// <c>mock.unserve</c>, when a run removes entries (ADR 0043).
    /// </summary>
    public UnserveMockCommandHandler(
        IConfigStore config,
        ISessionStore session,
        IServerProcessRegistry registry,
        IProcessLiveness liveness,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _config = config;
        _session = session;
        _registry = registry;
        _liveness = liveness;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Removes the served mock with the name.</summary>
    public async Task<Result<UnservedMock, UnserveMockError>> HandleAsync(
        UnserveMockCommand command,
        CancellationToken ct
    )
    {
        if (
            DeviceName.From(command.Name)
                is not Result<DeviceName, DeviceError>.Ok { Value: var deviceName }
            || ServerName.From(command.Name)
                is not Result<ServerName, ServerNameError>.Ok { Value: var serverName }
        )
        {
            return Fail(new UnserveMockInvalidName(command.Name));
        }

        var configLoad = await _config.LoadAsync(ct);
        if (configLoad is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Fail(
                new UnserveMockConfigFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)configLoad).Err
                )
            );
        }
        var sessionLoad = await _session.LoadAsync(ct);
        if (sessionLoad is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            return Fail(
                new UnserveMockSessionFailure(
                    ((Result<SessionState, SessionStoreError>.Error)sessionLoad).Err
                )
            );
        }

        if (config.FindDevice(deviceName) is not { } device)
        {
            return Fail(new UnserveMockDeviceNotFound(deviceName));
        }
        if (DeviceDifference(device, serverName, config) is { } deviceWhy)
        {
            return Fail(new UnserveMockDeviceConflict(deviceName, deviceWhy));
        }
        var server = config.FindServer(serverName);
        if (server is not null && ServerDifference(server, deviceName, config) is { } serverWhy)
        {
            return Fail(new UnserveMockServerConflict(serverName, serverWhy));
        }

        var record = await _registry.ReadAsync(serverName, ct);
        if (record is Result<ServerProcessEntry?, ServerProcessRegistryError>.Error recordErr)
        {
            return Fail(new UnserveMockRegistryFailure(recordErr.Err));
        }
        var entry = ((Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok)record).Value;
        if (entry is not null && _liveness.IsAlive(entry.ProcessId))
        {
            return Fail(new UnserveMockServerRunning(serverName, entry.ProcessId));
        }

        var updated = Removed(config, deviceName, server);
        if (await _config.SaveAsync(updated, ct) is Result<Unit, ConfigStoreError>.Error saveErr)
        {
            return Fail(new UnserveMockConfigFailure(saveErr.Err));
        }
        await _audit.AppendAsync(
            new ConfigMutated(_time.GetUtcNow(), "mock.unserve", deviceName.Value, _subject.Get()),
            ct
        );
        if (entry is not null)
        {
            _ = await _registry.DeleteAsync(serverName, ct);
        }

        if (session.GetActiveScenario(deviceName) is not null)
        {
            var unbound = session.UnbindScenario(deviceName);
            if (await _session.SaveAsync(unbound, ct) is Result<Unit, SessionStoreError>.Error err)
            {
                return Fail(new UnserveMockSessionFailure(err.Err));
            }
        }

        return Result.Success<UnservedMock, UnserveMockError>(
            new UnservedMock(deviceName, server?.Name)
        );
    }

    /// <summary>
    /// <paramref name="config"/> without the server, if any, and the device,
    /// which also takes the device's routes and a default naming it.
    /// </summary>
    private static ConfigDocument Removed(ConfigDocument config, DeviceName device, Server? server)
    {
        var withoutServer = server is null ? config : Expect(config.RemoveServer(server.Name));
        return Expect(withoutServer.RemoveDevice(device));
    }

    /// <summary>
    /// Why the device is not one <c>mock unserve</c> may remove;
    /// <see langword="null"/> when it is.
    /// </summary>
    private static string? DeviceDifference(Device device, ServerName own, ConfigDocument config)
    {
        if (device.Resource is not VisaResource.Mock)
        {
            return $"its resource is {device.Resource.ToCanonical()}, not {VisaResource.Mock.Canonical}";
        }
        var other = config.Routes.FirstOrDefault(r =>
            r.DeviceName == device.Name && r.ServerName != own
        );
        return other is null
            ? null
            : $"the server {other.ServerName.Value} routes {other.Endpoint.Value} to it";
    }

    /// <summary>
    /// Why the server is not one <c>mock serve</c> would have made for the
    /// device; <see langword="null"/> when it is.
    /// </summary>
    private static string? ServerDifference(Server server, DeviceName device, ConfigDocument config)
    {
        if (MockServerTypes.Endpoint(server.Type, server.Port) is not { } endpoint)
        {
            return $"its type is {MockServerTypes.Name(server.Type)}, which does not serve a mock";
        }
        var routes = config.Routes.Where(r => r.ServerName == server.Name).ToArray();
        if (routes.Length > 1)
        {
            return $"it has {routes.Length} routes";
        }
        if (
            routes.Length == 1
            && (routes[0].Endpoint.Value != endpoint || routes[0].DeviceName != device)
        )
        {
            return $"its route is {routes[0].Endpoint.Value} -> {routes[0].DeviceName.Value}, not {endpoint} -> {device.Value}";
        }
        return null;
    }

    private static ConfigDocument Expect(Result<ConfigDocument, ConfigError> result) =>
        result is Result<ConfigDocument, ConfigError>.Ok ok
            ? ok.Value
            : throw new InvalidOperationException("removing an entry found in the document failed");

    private static Result<UnservedMock, UnserveMockError> Fail(UnserveMockError error) =>
        Result.Failure<UnservedMock, UnserveMockError>(error);
}
