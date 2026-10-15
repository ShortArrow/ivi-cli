namespace IviCli.Application.Devices;

/// <summary>
/// Command DTO for adding a new device to the configuration. The fields are
/// the raw, untyped form as supplied by the CLI; the handler is responsible
/// for parsing them into Domain Value Objects (per ADR 0003 §2 Anti-Corruption
/// Layer).
/// </summary>
/// <param name="Name">Candidate device alias.</param>
/// <param name="Resource">Candidate VISA resource string.</param>
/// <param name="TimeoutMilliseconds">Per-device default timeout, in milliseconds.</param>
public sealed record AddDeviceCommand(string Name, string Resource, int TimeoutMilliseconds)
{
    /// <summary>
    /// Serial settings that differ from VISA's defaults; only an ASRL
    /// device takes them (ADR 0055 §4).
    /// </summary>
    public SerialOptions? Serial { get; init; }

    /// <summary>
    /// The terminator that ends a reply: <c>lf</c>, <c>cr</c> or <c>crlf</c>,
    /// or the same written with escapes, <c>\n</c>, <c>\r</c> or <c>\r\n</c>
    /// (ADR 0055 §5).
    /// </summary>
    public string? ReadTermination { get; init; }

    /// <summary>The terminator that ends a command, written as <see cref="ReadTermination"/> is.</summary>
    public string? WriteTermination { get; init; }
}

/// <summary>
/// Serial settings as supplied by the CLI; an omitted one keeps VISA's
/// default (ADR 0055 §4).
/// </summary>
/// <param name="Baud">The baud rate.</param>
/// <param name="DataBits">The data bits per character, 5 to 8.</param>
/// <param name="Parity"><c>none</c>, <c>odd</c>, <c>even</c>, <c>mark</c> or <c>space</c>.</param>
/// <param name="StopBits"><c>1</c>, <c>1.5</c> or <c>2</c>.</param>
/// <param name="FlowControl"><c>none</c>, <c>xon_xoff</c> or <c>rts_cts</c>.</param>
public sealed record SerialOptions(
    int? Baud = null,
    int? DataBits = null,
    string? Parity = null,
    string? StopBits = null,
    string? FlowControl = null
);
