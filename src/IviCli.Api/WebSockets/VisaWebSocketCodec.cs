using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IviCli.Api.WebSockets;

/// <summary>
/// Pure JSON codec for the VISA WebSocket subprotocol (ADR 0035).
/// One frame in / one frame out, no streaming state. Both sides of
/// the protocol are simple enough to hand-encode rather than pull a
/// generated serializer for sealed-record polymorphism.
/// </summary>
public static class VisaWebSocketCodec
{
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
                new ResponseWire("response", r.Scpi, r.Body, r.LatencyMs),
                WebSocketJsonContext.Default.ResponseWire
            ),
            VisaWebSocketEvent.Ack a => JsonSerializer.Serialize(
                new AckWire("ack", a.Scpi),
                WebSocketJsonContext.Default.AckWire
            ),
            VisaWebSocketEvent.Error e => JsonSerializer.Serialize(
                new ErrorWire("error", e.Code, e.Message),
                WebSocketJsonContext.Default.ErrorWire
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(ev)),
        };
        return Encoding.UTF8.GetBytes(json);
    }
}

// Server→client frame shapes (ADR 0035). Named records instead of the
// earlier anonymous objects so the source-generated serializer keeps the
// codec off the reflection path (trim/AOT, issue #15); the frames on the
// wire are unchanged.
internal sealed record ResponseWire(string Event, string Scpi, string Response, int LatencyMs);

internal sealed record AckWire(string Event, string Scpi);

internal sealed record ErrorWire(string Event, string Code, string Message);

/// <summary>Source-generated serializer for the WebSocket frames (issue #15).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ResponseWire))]
[JsonSerializable(typeof(AckWire))]
[JsonSerializable(typeof(ErrorWire))]
internal sealed partial class WebSocketJsonContext : JsonSerializerContext;
