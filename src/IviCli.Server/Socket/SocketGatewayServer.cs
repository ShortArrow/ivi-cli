using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using Microsoft.Extensions.Logging;

namespace IviCli.Server.Socket;

/// <summary>
/// Raw TCP SOCKET gateway: line-based SCPI (PRD §7.4 / ADR 0007).
/// Reads <c>\n</c>-terminated SCPI lines from the client, dispatches each
/// to the bound device's <see cref="IIviBackend"/>, and writes responses
/// back as <c>\n</c>-terminated lines. A trailing <c>?</c> in the line
/// distinguishes query from write.
/// </summary>
public sealed class SocketGatewayServer : IGatewayServer
{
    private readonly IBackendFactory _backendFactory;
    private readonly IScenarioBindingRefresher _refresher;
    private readonly ILogger<SocketGatewayServer> _logger;

    /// <summary>Creates a new server.</summary>
    public SocketGatewayServer(
        IBackendFactory backendFactory,
        ILogger<SocketGatewayServer> logger,
        IScenarioBindingRefresher? refresher = null
    )
    {
        _backendFactory = backendFactory;
        _logger = logger;
        _refresher = refresher ?? NullScenarioBindingRefresher.Instance;
    }

    /// <inheritdoc/>
    public ServerType SupportedType => ServerType.Socket;

    /// <inheritdoc/>
    public async Task<Result<Unit, GatewayServerError>> RunAsync(
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        if (!IPAddress.TryParse(server.Bind.Value, out var bindAddr))
        {
            bindAddr = IPAddress.Loopback;
        }
        var listener = new TcpListener(bindAddr, server.Port.Value);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            return Result.Failure<Unit, GatewayServerError>(
                new GatewayBindFailure(server.Bind, server.Port, ex.Message, ex)
            );
        }

        // Resolve the single SOCKET route + device upfront. The SOCKET
        // protocol exposes one endpoint per server (the bound port).
        var route = FindSocketRoute(server, config);
        Domain.Devices.Device? targetDevice = null;
        if (route is not null)
        {
            targetDevice = config.FindDevice(route.DeviceName);
        }

        _logger.LogInformation(
            "SOCKET gateway listening on {Bind}:{Port} (server {Name}, route {Endpoint} -> {Device})",
            server.Bind.Value,
            server.Port.Value,
            server.Name.Value,
            route?.Endpoint.Value ?? "(none)",
            route?.DeviceName.Value ?? "(none)"
        );

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // ADR 0015 §3: fire-and-forget per connection; the handler
                // catches everything and logs internally.
                _ = HandleConnectionAsync(client, route, targetDevice, ct);
            }
        }
        finally
        {
            listener.Stop();
        }

        _logger.LogInformation("SOCKET gateway stopped (server {Name})", server.Name.Value);
        return Result.Success<Unit, GatewayServerError>(Unit.Value);
    }

    private static Route? FindSocketRoute(Domain.Servers.Server server, ConfigDocument config)
    {
        var endpointKey = server.Port.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
        foreach (var r in config.Routes)
        {
            if (r.ServerName == server.Name && r.Endpoint.Value == endpointKey)
            {
                return r;
            }
        }
        foreach (var r in config.Routes)
        {
            if (r.ServerName == server.Name)
            {
                return r;
            }
        }
        return null;
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        Route? route,
        Domain.Devices.Device? device,
        CancellationToken ct
    )
    {
        using var scope = _logger.BeginScope(
            new
            {
                Protocol = "socket",
                RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
                RouteEndpoint = route?.Endpoint.Value ?? "(none)",
            }
        );

        try
        {
            using var tcp = client;
            using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true,
            };

            _logger.LogInformation("client connected");

            if (route is null || device is null)
            {
                _logger.LogWarning("no route / device configured; closing connection");
                return;
            }

            var backendResult = _backendFactory.CreateFor(device);
            if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
            {
                _logger.LogError("backend resolution failed");
                return;
            }

            var openResult = await backend.OpenAsync(device, ct);
            if (openResult is not Result<Unit, BackendError>.Ok)
            {
                _logger.LogError("backend open failed");
                return;
            }

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    if (line is null)
                    {
                        break;
                    }
                    var trimmed = line.TrimEnd('\r');
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    // Pick up an out-of-process scenario re-binding mid-connection:
                    // a client may hold one long-lived connection while a separate
                    // `mock scenario activate` runs. Refreshed per request so the
                    // change is observed without reconnecting. The refresher
                    // re-applies only when the bound scenario name changed (scene
                    // state is preserved otherwise), and is no-throw; guard anyway
                    // so a refresh failure never kills the connection.
                    try
                    {
                        await _refresher.RefreshAsync(device, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "scenario binding refresh failed; continuing");
                    }

                    if (trimmed.EndsWith('?'))
                    {
                        var queryResult = ScpiQuery.From(trimmed);
                        if (queryResult is not Result<ScpiQuery, ScpiError>.Ok { Value: var q })
                        {
                            _logger.LogWarning("invalid SCPI query received");
                            continue;
                        }
                        var resp = await backend.QueryAsync(device, q, ct);
                        if (resp is Result<string, BackendError>.Ok { Value: var responseText })
                        {
                            await writer.WriteLineAsync(responseText.AsMemory(), ct);
                        }
                        else
                        {
                            _logger.LogWarning("backend query failed");
                            break;
                        }
                    }
                    else
                    {
                        var cmdResult = ScpiCommand.From(trimmed);
                        if (cmdResult is not Result<ScpiCommand, ScpiError>.Ok { Value: var c })
                        {
                            _logger.LogWarning("invalid SCPI command received");
                            continue;
                        }
                        var writeResult = await backend.WriteAsync(device, c, ct);
                        if (writeResult is not Result<Unit, BackendError>.Ok)
                        {
                            _logger.LogWarning("backend write failed");
                            break;
                        }
                    }
                }
            }
            finally
            {
                _ = await backend.CloseAsync(device, ct);
            }

            _logger.LogInformation("client disconnected");
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "connection terminated with unexpected error");
        }
    }
}
