using System.Text;
using System.Text.Json;

namespace IviCli.Api.WebSockets;

/// <summary>
/// Pure JSON codec for the VISA WebSocket subprotocol (ADR 0035).
/// One frame in / one frame out, no streaming state. Both sides of
/// the protocol are simple enough to hand-encode rather than pull a
/// generated serializer for sealed-record polymorphism.
/// </summary>
public static class VisaWebSocketCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Decodes a client→server frame. Returns <see langword="null"/>
    /// on malformed JSON, unknown <c>op</c>, or a missing /
    /// whitespace-only <c>scpi</c> field. Caller emits a
    /// <see cref="VisaWebSocketErrorCodes.ProtocolError"/> event in
    /// that case.
    /// </summary>
    public static VisaWebSocketRequest? Decode(string frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return null;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(frame);
        }
        catch (JsonException)
        {
            return null;
        }
        using (doc)
        {
            if (
                !doc.RootElement.TryGetProperty("op", out var opProp)
                || opProp.ValueKind != JsonValueKind.String
            )
            {
                return null;
            }
            if (
                !doc.RootElement.TryGetProperty("scpi", out var scpiProp)
                || scpiProp.ValueKind != JsonValueKind.String
            )
            {
                return null;
            }
            var op = opProp.GetString();
            var scpi = scpiProp.GetString();
            if (string.IsNullOrWhiteSpace(scpi))
            {
                return null;
            }
            return op switch
            {
                "query" => new VisaWebSocketRequest.Query(scpi),
                "write" => new VisaWebSocketRequest.Write(scpi),
                _ => null,
            };
        }
    }

    /// <summary>
    /// Encodes a server→client frame to UTF-8 bytes ready for
    /// <c>WebSocket.SendAsync</c>. Always single-line; no trailing
    /// newline (WebSocket framing carries boundaries).
    /// </summary>
    public static byte[] EncodeFrame(VisaWebSocketEvent ev)
    {
        var json = ev switch
        {
            VisaWebSocketEvent.Response r => JsonSerializer.Serialize(
                new
                {
                    @event = "response",
                    scpi = r.Scpi,
                    response = r.Body,
                    latencyMs = r.LatencyMs,
                },
                JsonOptions
            ),
            VisaWebSocketEvent.Ack a => JsonSerializer.Serialize(
                new { @event = "ack", scpi = a.Scpi },
                JsonOptions
            ),
            VisaWebSocketEvent.Error e => JsonSerializer.Serialize(
                new
                {
                    @event = "error",
                    code = e.Code,
                    message = e.Message,
                },
                JsonOptions
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(ev)),
        };
        return Encoding.UTF8.GetBytes(json);
    }
}
