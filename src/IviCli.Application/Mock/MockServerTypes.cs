using System.Globalization;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;

namespace IviCli.Application.Mock;

/// <summary>
/// The four server types a mock is served by, and the endpoint and default
/// port each fixes (ADR 0056 §1, §2).
/// </summary>
internal static class MockServerTypes
{
    /// <summary>The port a VXI-11 mock listens on without <c>--port</c>.</summary>
    public const int DefaultVxi11Port = 1024;

    /// <summary>The type <paramref name="raw"/> names; <see langword="null"/> for any other.</summary>
    public static ServerType? Parse(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "socket" => ServerType.Socket,
            "hislip" => ServerType.HiSlip,
            "vxi11" => ServerType.Vxi11,
            "usbip" => ServerType.UsbIp,
            _ => null,
        };

    /// <summary>The port a mock of <paramref name="type"/> listens on without <c>--port</c>.</summary>
    public static int DefaultPort(ServerType type) =>
        type switch
        {
            ServerType.Socket => 5025,
            ServerType.HiSlip => 4880,
            ServerType.UsbIp => UsbIpConstants.DefaultPort,
            _ => DefaultVxi11Port,
        };

    /// <summary>
    /// The endpoint of the route to a mock on a server of <paramref name="type"/>
    /// listening on <paramref name="port"/>; <see langword="null"/> for a type
    /// that does not serve mocks.
    /// </summary>
    public static string? Endpoint(ServerType type, Port port) =>
        type switch
        {
            ServerType.Socket => port.Value.ToString(CultureInfo.InvariantCulture),
            ServerType.HiSlip => "hislip0",
            ServerType.Vxi11 => "inst0",
            ServerType.UsbIp => "1-1",
            _ => null,
        };

    /// <summary>The name a user gives <paramref name="type"/>, such as <c>socket</c>.</summary>
    public static string Name(ServerType type) => type.ToString().ToLowerInvariant();
}
