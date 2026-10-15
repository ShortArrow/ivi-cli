namespace IviCli.Domain.Devices;

/// <summary>
/// Errors that can arise from device-related operations in the domain.
/// </summary>
public abstract record DeviceError : IviError
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

/// <summary>
/// A <see cref="DeviceName"/> could not be constructed from the given raw input
/// because it failed format validation (empty, too long, or not matching the
/// permitted character pattern).
/// </summary>
/// <param name="Raw">The raw input string as provided by the caller.</param>
public sealed record InvalidDeviceNameFormat(string Raw) : DeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name format: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>A terminator was not LF, CR or CRLF (ADR 0055 §5).</summary>
/// <param name="Raw">The rejected characters.</param>
public sealed record InvalidTermination(string Raw) : DeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid termination {Raw}: use LF, CR or CRLF";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>A serial baud rate was not positive.</summary>
/// <param name="Value">The rejected baud rate.</param>
public sealed record InvalidBaudRate(int Value) : DeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid baud rate {Value}: must be positive";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Value };
}

/// <summary>
/// A serial data-bits value was outside <see cref="SerialSettings.MinDataBits"/>
/// to <see cref="SerialSettings.MaxDataBits"/>.
/// </summary>
/// <param name="Value">The rejected number of data bits.</param>
public sealed record InvalidDataBits(int Value) : DeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid data bits {Value}: must be 5 to 8";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Value };
}
