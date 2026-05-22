using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>
/// Outcomes the <c>visa add</c> use case can fail with. Per ADR 0014 §1 these
/// are an externally-declared sum type prefixed with the use-case name to
/// avoid collisions at the top of the Application namespace.
/// </summary>
public abstract record AddDeviceError : IviError
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

/// <summary>The candidate name failed <see cref="DeviceName"/> validation.</summary>
/// <param name="Raw">The raw name string as supplied.</param>
public sealed record AddDeviceInvalidName(string Raw) : AddDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The candidate VISA resource failed parsing.</summary>
/// <param name="Raw">The raw resource string as supplied.</param>
public sealed record AddDeviceInvalidResource(string Raw) : AddDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid VISA resource: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The candidate timeout failed validation (negative or above the maximum).</summary>
/// <param name="RawMilliseconds">The raw millisecond value as supplied.</param>
public sealed record AddDeviceInvalidTimeout(int RawMilliseconds) : AddDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid timeout: {RawMilliseconds}ms";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { RawMilliseconds };
}

/// <summary>The configuration already contains a device with the supplied alias.</summary>
/// <param name="Name">The conflicting device alias.</param>
public sealed record AddDeviceNameTaken(DeviceName Name) : AddDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device name already taken: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// The underlying <see cref="IConfigStore"/> could not be read or written.
/// The inner error preserves the storage-layer detail for the diagnostic
/// logging path described in ADR 0014 §9.
/// </summary>
/// <param name="Inner">The propagated <see cref="ConfigStoreError"/>.</param>
public sealed record AddDeviceStorageFailure(ConfigStoreError Inner) : AddDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}
