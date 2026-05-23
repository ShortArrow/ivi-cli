using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Socket;

/// <summary>
/// Client-side <see cref="IIviBackend"/> for raw TCP SOCKET endpoints
/// (PRD §7.4 / ADR 0007). Resolves the host:port from the device's
/// <see cref="VisaResource.Tcpip"/> when its <c>LanDevice</c> looks like
/// a SOCKET-style port (e.g. <c>5025</c>), opens a TCP connection on each
/// session, and sends <c>\n</c>-terminated SCPI.
/// </summary>
public sealed class SocketBackend : IIviBackend
{
    // Per-device session state. One open connection per Device alias is
    // kept in this dictionary for the lifetime of the process; the SOCKET
    // protocol is stateless enough that we can re-open on demand.
    private readonly Dictionary<DeviceName, SocketSession> _sessions = new();
    private readonly object _gate = new();

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (device.Resource is not VisaResource.Tcpip tcpip)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("SocketBackend only handles TCPIP::host::port::SOCKET")
            );
        }

        var portText = tcpip.LanDevice;
        if (
            !int.TryParse(
                portText,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var port
            )
        )
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected(
                    $"SocketBackend expects a numeric LanDevice (got '{portText}')"
                )
            );
        }

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(tcpip.Host, port, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            client.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"connect failed: {ex.Message}", ex)
            );
        }

        lock (_gate)
        {
            if (_sessions.TryGetValue(device.Name, out var existing))
            {
                existing.Dispose();
            }
            _sessions[device.Name] = new SocketSession(client);
        }
        return Result.Success<Unit, BackendError>(Unit.Value);
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_sessions.Remove(device.Name, out var session))
            {
                session.Dispose();
            }
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        SocketSession? session;
        lock (_gate)
        {
            _sessions.TryGetValue(device.Name, out session);
        }
        if (session is null)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("socket session not open")
            );
        }
        try
        {
            await session.Writer.WriteLineAsync(command.Value.AsMemory(), ct);
            await session.Writer.FlushAsync(ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"write failed: {ex.Message}", ex)
            );
        }
        return Result.Success<Unit, BackendError>(Unit.Value);
    }

    /// <inheritdoc/>
    public async Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        SocketSession? session;
        lock (_gate)
        {
            _sessions.TryGetValue(device.Name, out session);
        }
        if (session is null)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected("socket session not open")
            );
        }
        try
        {
            await session.Writer.WriteLineAsync(query.Value.AsMemory(), ct);
            await session.Writer.FlushAsync(ct);
            var response = await session.Reader.ReadLineAsync(ct);
            if (response is null)
            {
                return Result.Failure<string, BackendError>(
                    new TransportDisconnected("connection closed during query")
                );
            }
            return Result.Success<string, BackendError>(response);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected($"query failed: {ex.Message}", ex)
            );
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SocketSession? session;
        lock (_gate)
        {
            _sessions.TryGetValue(device.Name, out session);
        }
        if (session is null)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected("socket session not open")
            );
        }
        try
        {
            var response = await session.Reader.ReadLineAsync(ct);
            return response is null
                ? Result.Failure<string, BackendError>(
                    new TransportDisconnected("connection closed")
                )
                : Result.Success<string, BackendError>(response);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected($"read failed: {ex.Message}", ex)
            );
        }
    }

    private sealed class SocketSession : IDisposable
    {
        private readonly TcpClient _client;
        public StreamReader Reader { get; }
        public StreamWriter Writer { get; }

        public SocketSession(TcpClient client)
        {
            _client = client;
            var stream = client.GetStream();
            Reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            Writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = false,
            };
        }

        public void Dispose()
        {
            try
            {
                Writer.Dispose();
            }
            catch
            {
                /* swallow */
            }
            try
            {
                Reader.Dispose();
            }
            catch
            {
                /* swallow */
            }
            _client.Dispose();
        }
    }
}
