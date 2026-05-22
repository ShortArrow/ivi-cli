using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Session;

/// <summary>Query DTO for reading the current device pointer (PRD §6.2 visa current).</summary>
public sealed record GetCurrentDeviceQuery;

/// <summary>
/// The query result: the current device (or <see langword="null"/> if no
/// current device is set in either session.json or [defaults].device).
/// </summary>
public sealed record CurrentDevice(DeviceName? Name);

/// <summary>Errors that the <c>visa current</c> query can fail with.</summary>
public abstract record GetCurrentDeviceError : IviError
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

/// <summary>The session store could not be read.</summary>
public sealed record GetCurrentDeviceSessionFailure(SessionStoreError Inner) : GetCurrentDeviceError
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
