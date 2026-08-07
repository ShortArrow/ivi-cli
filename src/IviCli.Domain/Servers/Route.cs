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
public sealed record Route(ServerName ServerName, PublicEndpoint Endpoint, DeviceName DeviceName)
{
    /// <summary>
    /// The USB profile the exported device presents, read by the USB/IP
    /// device server and by nothing else (ADR 0049 §5).
    ///
    /// A route on any other server type carries the value and never acts
    /// on it. The configuration's cross-entity invariants are existence
    /// and uniqueness — no rule anywhere pairs a route's fields against
    /// its server's type — so rejecting a profile on a LAN route would be
    /// the only such rule in the document, and the field means nothing
    /// there rather than meaning something wrong.
    /// </summary>
    public UsbExportProfile Profile { get; init; } = UsbExportProfile.UsbTmc;
}
