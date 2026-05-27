using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Api.WebSockets;

/// <summary>
/// Maps <c>GET /v1/devices/{name}/visa</c> as a WebSocket endpoint
/// (ADR 0035). One client connection = one VISA session bound to the
/// path's device alias. Each inbound text frame is a SCPI query or
/// write; replies are emitted on the same socket.
/// </summary>
public static class VisaWebSocketEndpoint
{
    private const int CloseCodeDeviceNotFound = 4404;
    private const int CloseCodeInternalError = 1011;
    private const int MaxFrameBytes = 64 * 1024;

    /// <summary>Attaches the WebSocket route to the supplied router.</summary>
    public static IEndpointRouteBuilder MapVisaWebSocket(this IEndpointRouteBuilder app)
    {
        app.Map("/v1/devices/{name}/visa", HandleAsync).WithName("VisaWebSocket");
        return app;
    }

    private static async Task HandleAsync(HttpContext context, string name)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(
                "this endpoint requires a WebSocket upgrade (RFC 6455)."
            );
            return;
        }

        var services = context.RequestServices;
        var configStore = services.GetRequiredService<IConfigStore>();
        var query = services.GetRequiredService<QueryDeviceCommandHandler>();
        var write = services.GetRequiredService<WriteDeviceCommandHandler>();
        var ct = context.RequestAborted;

        var configResult = await configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            using var earlyClose = await context.WebSockets.AcceptWebSocketAsync();
            await CloseWithReasonAsync(
                earlyClose,
                WebSocketCloseStatus.InternalServerError,
                "config store failure",
                ct
            );
            return;
        }

        if (
            DeviceName.From(name) is not Result<DeviceName, DeviceError>.Ok { Value: var dn }
            || config.FindDevice(dn) is null
        )
        {
            using var deviceMissing = await context.WebSockets.AcceptWebSocketAsync();
            // Emit a structured error event first so the client receives a
            // diagnosable message, then close with a normal-closure code.
            // RFC 6455 reserves 4xxx for private use, but TestHost surfaces
            // those inconsistently across .NET versions; the error event is
            // the canonical signal.
            await SendAsync(
                deviceMissing,
                new VisaWebSocketEvent.Error(
                    VisaWebSocketErrorCodes.DeviceNotFound,
                    $"device '{name}' is not registered"
                ),
                ct
            );
            await deviceMissing.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "device_not_found",
                ct
            );
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[MaxFrameBytes];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, ct);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "client closed",
                        ct
                    );
                    return;
                }
                if (received.MessageType != WebSocketMessageType.Text)
                {
                    await SendAsync(
                        socket,
                        new VisaWebSocketEvent.Error(
                            VisaWebSocketErrorCodes.ProtocolError,
                            "binary frames are not supported in v1"
                        ),
                        ct
                    );
                    continue;
                }
                if (!received.EndOfMessage)
                {
                    await SendAsync(
                        socket,
                        new VisaWebSocketEvent.Error(
                            VisaWebSocketErrorCodes.ProtocolError,
                            "fragmented frames are not supported in v1"
                        ),
                        ct
                    );
                    continue;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
                var request = VisaWebSocketCodec.Decode(text);
                if (request is null)
                {
                    await SendAsync(
                        socket,
                        new VisaWebSocketEvent.Error(
                            VisaWebSocketErrorCodes.ProtocolError,
                            "frame is not a recognised request shape"
                        ),
                        ct
                    );
                    continue;
                }

                var ev = request switch
                {
                    VisaWebSocketRequest.Query q => await HandleQueryAsync(query, name, q, ct),
                    VisaWebSocketRequest.Write w => await HandleWriteAsync(write, name, w, ct),
                    _ => new VisaWebSocketEvent.Error(
                        VisaWebSocketErrorCodes.ProtocolError,
                        "unknown request"
                    ),
                };
                await SendAsync(socket, ev, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            try
            {
                await SendAsync(
                    socket,
                    new VisaWebSocketEvent.Error(VisaWebSocketErrorCodes.InternalError, ex.Message),
                    CancellationToken.None
                );
                await socket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "internal_error",
                    CancellationToken.None
                );
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private static async Task<VisaWebSocketEvent> HandleQueryAsync(
        QueryDeviceCommandHandler handler,
        string device,
        VisaWebSocketRequest.Query req,
        CancellationToken ct
    )
    {
        var sw = Stopwatch.StartNew();
        var result = await handler.HandleAsync(new QueryDeviceCommand(device, req.Scpi), ct);
        sw.Stop();
        return result switch
        {
            Result<string, QueryDeviceError>.Ok ok => new VisaWebSocketEvent.Response(
                req.Scpi,
                ok.Value,
                (int)sw.Elapsed.TotalMilliseconds
            ),
            Result<string, QueryDeviceError>.Error err => MapQueryError(err.Err),
            _ => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.InternalError,
                "unknown result variant"
            ),
        };
    }

    private static async Task<VisaWebSocketEvent> HandleWriteAsync(
        WriteDeviceCommandHandler handler,
        string device,
        VisaWebSocketRequest.Write req,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new WriteDeviceCommand(device, req.Scpi), ct);
        return result switch
        {
            Result<Unit, WriteDeviceError>.Ok => new VisaWebSocketEvent.Ack(req.Scpi),
            Result<Unit, WriteDeviceError>.Error err => MapWriteError(err.Err),
            _ => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.InternalError,
                "unknown result variant"
            ),
        };
    }

    private static VisaWebSocketEvent MapQueryError(QueryDeviceError error) =>
        error switch
        {
            QueryDeviceInvalidScpi s => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.InvalidScpi,
                s.Reason
            ),
            QueryDeviceInvalidName or QueryDeviceUnknown or QueryDeviceNoTarget =>
                new VisaWebSocketEvent.Error(
                    VisaWebSocketErrorCodes.DeviceNotFound,
                    "device not registered"
                ),
            QueryDeviceTransportFailure t => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.BackendFailure,
                t.Inner.Message
            ),
            QueryDeviceConfigFailure or QueryDeviceSessionFailure => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.ConfigStoreFailure,
                error.Message
            ),
            _ => new VisaWebSocketEvent.Error(VisaWebSocketErrorCodes.InternalError, error.Message),
        };

    private static VisaWebSocketEvent MapWriteError(WriteDeviceError error) =>
        error switch
        {
            WriteDeviceInvalidScpi s => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.InvalidScpi,
                s.Reason
            ),
            WriteDeviceInvalidName or WriteDeviceUnknown or WriteDeviceNoTarget =>
                new VisaWebSocketEvent.Error(
                    VisaWebSocketErrorCodes.DeviceNotFound,
                    "device not registered"
                ),
            WriteDeviceTransportFailure t => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.BackendFailure,
                t.Inner.Message
            ),
            WriteDeviceConfigFailure or WriteDeviceSessionFailure => new VisaWebSocketEvent.Error(
                VisaWebSocketErrorCodes.ConfigStoreFailure,
                error.Message
            ),
            _ => new VisaWebSocketEvent.Error(VisaWebSocketErrorCodes.InternalError, error.Message),
        };

    private static Task SendAsync(WebSocket socket, VisaWebSocketEvent ev, CancellationToken ct)
    {
        var bytes = VisaWebSocketCodec.EncodeFrame(ev);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static Task CloseWithReasonAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string reason,
        CancellationToken ct
    ) => socket.CloseAsync(status, reason, ct);

    private static Task CloseWithCustomCodeAsync(
        WebSocket socket,
        int code,
        string reason,
        CancellationToken ct
    ) => socket.CloseAsync((WebSocketCloseStatus)code, reason, ct);
}
