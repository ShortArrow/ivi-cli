namespace IviCli.Api.WebSockets;

/// <summary>
/// Inbound text frames carried over the VISA WebSocket subprotocol
/// (ADR 0035). Each frame is one JSON object; the static codec
/// (<see cref="VisaWebSocketCodec"/>) normalises to one of the
/// sealed sub-records below.
/// </summary>
public abstract record VisaWebSocketRequest
{
    private VisaWebSocketRequest() { }

    /// <summary>Client asked the server to send a SCPI query (expects a response).</summary>
    public sealed record Query(string Scpi) : VisaWebSocketRequest;

    /// <summary>Client asked the server to send a SCPI write (expects no response).</summary>
    public sealed record Write(string Scpi) : VisaWebSocketRequest;
}

/// <summary>
/// Outbound text frames the server emits. Each event encodes to a
/// single-line JSON object with a stable <c>event</c> discriminator.
/// </summary>
public abstract record VisaWebSocketEvent
{
    private VisaWebSocketEvent() { }

    /// <summary>Query reply — the SCPI prompt that was sent + the device response.</summary>
    public sealed record Response(string Scpi, string Body, int LatencyMs) : VisaWebSocketEvent;

    /// <summary>Write acknowledgement — the SCPI command the server accepted.</summary>
    public sealed record Ack(string Scpi) : VisaWebSocketEvent;

    /// <summary>Server failure for the most recent client frame.</summary>
    public sealed record Error(string Code, string Message) : VisaWebSocketEvent;
}

/// <summary>
/// Stable error-code strings emitted in <see cref="VisaWebSocketEvent.Error"/>.
/// Locked in step with the HTTP envelope codes from ADR 0034 §3 so
/// browser clients can share an error map across the two transports.
/// </summary>
public static class VisaWebSocketErrorCodes
{
    /// <summary>The client frame did not decode as a known request shape.</summary>
    public const string ProtocolError = "protocol_error";

    /// <summary>The <c>scpi</c> field was empty or absent.</summary>
    public const string MissingScpi = "missing_scpi";

    /// <summary>The SCPI text failed Application-layer validation.</summary>
    public const string InvalidScpi = "invalid_scpi";

    /// <summary>The path device alias is not registered.</summary>
    public const string DeviceNotFound = "device_not_found";

    /// <summary>The backend reported an IO / transport failure.</summary>
    public const string BackendFailure = "backend_failure";

    /// <summary>The config or session store could not be read.</summary>
    public const string ConfigStoreFailure = "config_store_failure";

    /// <summary>Unexpected server-side failure (catch-all).</summary>
    public const string InternalError = "internal_error";
}
