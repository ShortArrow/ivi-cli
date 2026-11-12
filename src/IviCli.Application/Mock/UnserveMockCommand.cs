using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for <c>mock unserve</c>: remove what <c>mock serve</c>
/// registered under a name (ADR 0056 §7).
/// </summary>
/// <param name="Name">
/// The name of the device and the server: the <c>--alias</c> given to
/// <c>mock serve</c>, or the name it printed.
/// </param>
public sealed record UnserveMockCommand(string Name);

/// <summary>What <c>mock unserve</c> removed.</summary>
/// <param name="Device">The <c>MOCK::INSTR</c> device, whose binding was removed with it.</param>
/// <param name="Server">The server with the same name, or <see langword="null"/> when there was none.</param>
public sealed record UnservedMock(DeviceName Device, ServerName? Server);

/// <summary>Outcomes <c>mock unserve</c> can fail with; each leaves every entry in place.</summary>
public abstract record UnserveMockError : IviError
{
    /// <inheritdoc/>
    public virtual LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The name is not a valid device and server name.</summary>
public sealed record UnserveMockInvalidName(string Raw) : UnserveMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No device has the name.</summary>
public sealed record UnserveMockDeviceNotFound(DeviceName Name) : UnserveMockError
{
    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// The device is not one <c>mock serve</c> made, or another server routes
/// to it.
/// </summary>
/// <param name="Name">The device.</param>
/// <param name="Difference">What makes it so, such as its resource.</param>
public sealed record UnserveMockDeviceConflict(DeviceName Name, string Difference)
    : UnserveMockError
{
    /// <inheritdoc/>
    public override string Message => "device {Name} is not a served mock: {Difference}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Difference };
}

/// <summary>The server with the name is not one <c>mock serve</c> made.</summary>
/// <param name="Name">The server.</param>
/// <param name="Difference">What makes it so, such as a second route.</param>
public sealed record UnserveMockServerConflict(ServerName Name, string Difference)
    : UnserveMockError
{
    /// <inheritdoc/>
    public override string Message => "server {Name} is not a served mock: {Difference}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Difference };
}

/// <summary>The process recorded under the name is running.</summary>
public sealed record UnserveMockServerRunning(ServerName Name, int ProcessId) : UnserveMockError
{
    /// <inheritdoc/>
    public override string Message => "server {Name} is running as process {ProcessId}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, ProcessId };
}

/// <summary>The process record under the name could not be read.</summary>
public sealed record UnserveMockRegistryFailure(ServerProcessRegistryError Inner) : UnserveMockError
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

/// <summary><c>config.toml</c> could not be read or written; nothing was removed.</summary>
public sealed record UnserveMockConfigFailure(ConfigStoreError Inner) : UnserveMockError
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

/// <summary>
/// The session could not be read, and nothing was removed, or could not be
/// written after the device and server were, which leaves the binding of a
/// device that no longer exists.
/// </summary>
public sealed record UnserveMockSessionFailure(SessionStoreError Inner) : UnserveMockError
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
