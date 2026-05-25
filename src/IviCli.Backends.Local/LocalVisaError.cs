using IviCli.Domain;

namespace IviCli.Backends.Local;

/// <summary>
/// Vendor-neutral failure shape from <see cref="IVisaSessionFactory"/>
/// and <see cref="IVisaSessionHandle"/>. The Backend translates these
/// into <see cref="IviCli.Application.Backends.BackendError"/> variants.
/// </summary>
public abstract record LocalVisaError : IviError
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

/// <summary>The VISA runtime is not installed or could not be loaded.</summary>
public sealed record LocalVisaRuntimeMissing(string Detail) : LocalVisaError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "VISA runtime not available: {Detail}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Detail };
}

/// <summary>VISA resource string was rejected by the runtime.</summary>
public sealed record LocalVisaOpenFailure(string Resource, string Detail, Exception? Inner)
    : LocalVisaError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "VISA open failed for {Resource}: {Detail}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Resource, Detail };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}

/// <summary>An IO operation against an open session failed.</summary>
public sealed record LocalVisaIoFailure(string Detail, Exception? Inner) : LocalVisaError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "VISA IO failure: {Detail}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Detail };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}
