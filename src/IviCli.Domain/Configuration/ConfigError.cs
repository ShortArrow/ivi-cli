using IviCli.Domain.Devices;
using IviCli.Domain.Servers;

namespace IviCli.Domain.Configuration;

/// <summary>
/// Errors that can arise from <see cref="ConfigDocument"/> mutation operations.
/// </summary>
public abstract record ConfigError : IviError
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
/// An attempt was made to add a device whose name is already present in the configuration.
/// </summary>
/// <param name="Name">The conflicting device name.</param>
public sealed record DuplicateDeviceName(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "duplicate device name: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// A device that is not on a serial line carried serial settings (ADR 0055 §4).
/// </summary>
/// <param name="Name">The device.</param>
public sealed record SerialSettingsOnNonSerialDevice(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device {Name} has serial settings but is not an ASRL device";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// An <c>[[asrl]]</c> row had a negative board or a port name that an ASRL
/// resource cannot carry: empty, only digits, or containing <c>::</c>.
/// </summary>
/// <param name="Board">The board number given.</param>
/// <param name="Port">The port name given.</param>
public sealed record InvalidAsrlBoardRegistration(int Board, string Port) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid ASRL board {Board} for port {Port}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Board, Port };
}

/// <summary>A board number was registered twice (ADR 0055 §3).</summary>
/// <param name="Board">The board number.</param>
public sealed record DuplicateAsrlBoard(int Board) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "ASRL board {Board} is already registered";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Board };
}

/// <summary>A port was registered under a second board (ADR 0055 §3).</summary>
/// <param name="Port">The port.</param>
/// <param name="Board">The board it is already registered under.</param>
public sealed record DuplicateAsrlPort(string Port, int Board) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "port {Port} is already registered as ASRL board {Board}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Port, Board };
}

/// <summary>An operation referenced a board number that is not registered.</summary>
/// <param name="Board">The board number.</param>
public sealed record AsrlBoardNotFound(int Board) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "ASRL board {Board} is not registered";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Board };
}

/// <summary>
/// An operation referenced a device name that does not exist in the configuration.
/// </summary>
/// <param name="Name">The missing device name.</param>
public sealed record DeviceNotFound(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// An attempt was made to set the default device to a name that is not present
/// in the configuration's device collection.
/// </summary>
/// <param name="Name">The candidate default name that has no matching device.</param>
public sealed record DefaultDeviceMissing(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "default device not configured: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>A server with the same name already exists.</summary>
public sealed record DuplicateServerName(ServerName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "duplicate server name: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The referenced server does not exist.</summary>
public sealed record ServerNotFound(ServerName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "server not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>A route's owning server does not exist in the configuration.</summary>
public sealed record RouteServerMissing(ServerName Server) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "route references missing server: {Server}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Server };
}

/// <summary>A route's target device does not exist in the configuration.</summary>
public sealed record RouteDeviceMissing(DeviceName Device) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "route references missing device: {Device}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Device };
}

/// <summary>A route with the same (server, public endpoint) pair already exists.</summary>
public sealed record DuplicateRoute(ServerName Server, PublicEndpoint Endpoint) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "duplicate route: {Server}/{Endpoint}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Server, Endpoint };
}

/// <summary>The referenced route does not exist.</summary>
public sealed record RouteNotFound(ServerName Server, PublicEndpoint Endpoint) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "route not found: {Server}/{Endpoint}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Server, Endpoint };
}
