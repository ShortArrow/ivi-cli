using IviCli.Domain.Devices;

namespace IviCli.Domain.Servers;

/// <summary>
/// Binds a public endpoint exposed by a gateway server to a locally
/// configured <see cref="DeviceName"/>. At connect time the gateway uses
/// the (<see cref="ServerName"/>, <see cref="Endpoint"/>) pair to look up
/// which device to forward to (ADR 0007 §7).
/// </summary>
/// <param name="ServerName">The owning gateway server.</param>
/// <param name="Endpoint">The public endpoint name (HiSLIP name or SOCKET port).</param>
/// <param name="DeviceName">The locally-configured device to forward to.</param>
public sealed record Route(ServerName ServerName, PublicEndpoint Endpoint, DeviceName DeviceName);
