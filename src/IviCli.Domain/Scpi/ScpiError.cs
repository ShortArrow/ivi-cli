namespace IviCli.Domain.Scpi;

/// <summary>Errors that can arise from SCPI Value Object construction.</summary>
public abstract record ScpiError : IviError
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

/// <summary>The raw input could not be constructed into a <see cref="ScpiCommand"/>.</summary>
public sealed record InvalidScpiCommand(string Raw, string Reason) : ScpiError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid SCPI command: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}

/// <summary>The raw input could not be constructed into a <see cref="ScpiQuery"/>.</summary>
public sealed record InvalidScpiQuery(string Raw, string Reason) : ScpiError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid SCPI query: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}
