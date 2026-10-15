namespace IviCli.Domain.Devices;

/// <summary>
/// The sequence that ends a message on a transport without a message end
/// of its own, SOCKET and serial (ADR 0055 §5): LF, CR or CRLF.
/// </summary>
public sealed record Termination
{
    /// <summary>Line feed, <c>"\n"</c>.</summary>
    public static Termination Lf { get; } = new("\n");

    /// <summary>Carriage return, <c>"\r"</c>.</summary>
    public static Termination Cr { get; } = new("\r");

    /// <summary>Carriage return and line feed, <c>"\r\n"</c>.</summary>
    public static Termination CrLf { get; } = new("\r\n");

    /// <summary>The characters of the sequence.</summary>
    public string Value { get; }

    private Termination(string value) => Value = value;

    /// <summary>The terminator <paramref name="raw"/> spells.</summary>
    /// <returns>
    /// The terminator, or <see cref="InvalidTermination"/> unless
    /// <paramref name="raw"/> is LF, CR or CRLF.
    /// </returns>
    public static Result<Termination, DeviceError> From(string raw) =>
        raw switch
        {
            "\n" => Result.Success<Termination, DeviceError>(Lf),
            "\r" => Result.Success<Termination, DeviceError>(Cr),
            "\r\n" => Result.Success<Termination, DeviceError>(CrLf),
            _ => Result.Failure<Termination, DeviceError>(new InvalidTermination(raw)),
        };
}
