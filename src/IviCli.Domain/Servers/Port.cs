namespace IviCli.Domain.Servers;

/// <summary>
/// A TCP port number in the 1..65535 range. Used both for bind ports
/// (gateway listener) and for SOCKET-style public endpoints.
/// </summary>
public sealed record Port
{
    /// <summary>The minimum legal port number.</summary>
    public const int Min = 1;

    /// <summary>The maximum legal port number.</summary>
    public const int Max = 65535;

    /// <summary>The well-known SOCKET / raw-TCP port for SCPI instruments.</summary>
    public static Port DefaultSocket { get; } =
        From(5025) is Result<Port, PortError>.Ok ok
            ? ok.Value
            : throw new InvalidOperationException();

    /// <summary>The IVI Foundation registered HiSLIP port.</summary>
    public static Port DefaultHiSlip { get; } =
        From(4880) is Result<Port, PortError>.Ok ok
            ? ok.Value
            : throw new InvalidOperationException();

    /// <summary>The numeric port value.</summary>
    public int Value { get; }

    private Port(int value) => Value = value;

    /// <summary>Validates and constructs a <see cref="Port"/>.</summary>
    public static Result<Port, PortError> From(int raw)
    {
        if (raw < Min || raw > Max)
        {
            return Result.Failure<Port, PortError>(new PortOutOfRange(raw));
        }
        return Result.Success<Port, PortError>(new Port(raw));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Errors that can arise when constructing a <see cref="Port"/>.</summary>
public abstract record PortError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The supplied port number is outside the legal range.</summary>
public sealed record PortOutOfRange(int Raw) : PortError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "port out of range (1..65535): {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
