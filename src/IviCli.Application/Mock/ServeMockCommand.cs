using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Servers;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for <c>mock serve</c>: serve one scenario at one address
/// (ADR 0056 §1). The fields are the raw form the CLI supplies.
/// </summary>
/// <param name="Scenario">The scenario to serve, which must already exist.</param>
/// <param name="Type"><c>socket</c>, <c>hislip</c>, <c>vxi11</c> or <c>usbip</c>.</param>
/// <param name="Port">
/// The port to listen on; <see langword="null"/> takes the type's default:
/// 5025, 4880, 1024 and 3240 in the order of <paramref name="Type"/>.
/// </param>
/// <param name="Alias">
/// The name of the device and the server; <see langword="null"/> takes
/// <c>mock-&lt;type&gt;-&lt;port&gt;</c>.
/// </param>
/// <param name="Bind">The address to listen on.</param>
/// <param name="Profile">
/// <c>usbtmc</c> or <c>cdc-acm</c>, the USB profile of a <c>usbip</c> route;
/// <see langword="null"/> takes <c>usbtmc</c>.
/// </param>
public sealed record ServeMockCommand(
    string Scenario,
    string Type,
    int? Port = null,
    string? Alias = null,
    string Bind = "127.0.0.1",
    string? Profile = null
);

/// <summary>
/// The entries <c>mock serve</c> serves the scenario through, each found
/// or registered: the <c>MOCK::INSTR</c> device bound to the scenario, the
/// server and its one route to the device.
/// </summary>
public sealed record ServedMock(ScenarioName Scenario, Device Device, Server Server, Route Route);

/// <summary>Outcomes <c>mock serve</c> can fail with before the server starts.</summary>
public abstract record ServeMockError : IviError
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

/// <summary>The scenario name failed validation.</summary>
public sealed record ServeMockInvalidScenario(string Raw) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No scenario has that name.</summary>
public sealed record ServeMockScenarioNotFound(ScenarioName Name) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The type is not one a mock is served by.</summary>
public sealed record ServeMockInvalidType(string Raw) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid mock server type: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The port is out of range.</summary>
public sealed record ServeMockInvalidPort(int Raw) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid port: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The bind address failed validation.</summary>
public sealed record ServeMockInvalidBind(string Raw) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid bind: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The USB profile is neither <c>usbtmc</c> nor <c>cdc-acm</c>.</summary>
public sealed record ServeMockInvalidProfile(string Raw) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid USB profile: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The alias is not a valid device and server name.</summary>
public sealed record ServeMockInvalidAlias(string Raw) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "invalid alias: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The name is the one <c>api start</c> records its process under.</summary>
public sealed record ServeMockReservedName(ServerName Name) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "the name {Name} is reserved";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// A device with the name exists and is not one <c>mock serve</c> may reuse
/// (ADR 0056 §3).
/// </summary>
/// <param name="Name">The device.</param>
/// <param name="Difference">What makes it differ, such as its resource.</param>
public sealed record ServeMockDeviceConflict(DeviceName Name, string Difference) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "device {Name} cannot serve the mock: {Difference}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Difference };
}

/// <summary>
/// A server with the name exists and does not match the request
/// (ADR 0056 §4).
/// </summary>
/// <param name="Name">The server.</param>
/// <param name="Difference">What makes it differ, such as its port.</param>
public sealed record ServeMockServerConflict(ServerName Name, string Difference) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "server {Name} cannot serve the mock: {Difference}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Difference };
}

/// <summary>The process recorded for the server is running (ADR 0056 §6).</summary>
public sealed record ServeMockServerRunning(ServerName Name, int ProcessId) : ServeMockError
{
    /// <inheritdoc/>
    public override string Message => "server {Name} is running as process {ProcessId}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, ProcessId };
}

/// <summary>The server's process record could not be read.</summary>
public sealed record ServeMockRegistryFailure(ServerProcessRegistryError Inner) : ServeMockError
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

/// <summary>The scenario store could not be read.</summary>
public sealed record ServeMockScenarioStoreFailure(ScenarioStoreError Inner) : ServeMockError
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
/// <c>config.toml</c> could not be read or written; when it could not be
/// written, nothing was registered.
/// </summary>
public sealed record ServeMockConfigFailure(ConfigStoreError Inner) : ServeMockError
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
/// The session could not be read or written. When it could not be written,
/// the device, server and route are registered and the binding is not, and
/// running <c>mock serve</c> again completes it.
/// </summary>
public sealed record ServeMockSessionFailure(SessionStoreError Inner) : ServeMockError
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
