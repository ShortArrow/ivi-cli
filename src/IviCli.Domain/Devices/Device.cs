using IviCli.Domain.Visa;

namespace IviCli.Domain.Devices;

/// <summary>
/// A configured instrument, identified by an alias <see cref="DeviceName"/>
/// and addressed via a <see cref="VisaResource"/> with a per-device
/// <see cref="Domain.Timeout"/>.
/// </summary>
/// <remarks>
/// <para>
/// All component fields are Value Objects with their own validation, so the
/// record itself trusts its inputs. Higher-level construction (parsing config
/// or CLI arguments) lives in the Application layer.
/// </para>
/// <para>
/// Equality is structural by default. The uniqueness invariant on
/// <see cref="DeviceName"/> within a configuration is enforced at the
/// <c>ConfigDocument</c> level, not on this record.
/// </para>
/// </remarks>
/// <param name="Name">The unique alias.</param>
/// <param name="Resource">The VISA resource that addresses the instrument.</param>
/// <param name="Timeout">The per-device default operation timeout.</param>
/// <param name="Serial">
/// The serial line's settings, which only an ASRL device has (ADR 0055 §4);
/// <see langword="null"/> means VISA's defaults.
/// </param>
/// <param name="ReadTermination">
/// The sequence that ends a reply on SOCKET and serial; <see langword="null"/>
/// keeps each backend's own rule (ADR 0055 §5).
/// </param>
/// <param name="WriteTermination">
/// The sequence that ends a command on SOCKET and serial; <see langword="null"/>
/// means LF (ADR 0055 §5).
/// </param>
public sealed record Device(
    DeviceName Name,
    VisaResource Resource,
    Timeout Timeout,
    SerialSettings? Serial = null,
    Termination? ReadTermination = null,
    Termination? WriteTermination = null
)
{
    /// <summary>
    /// The serial line's settings, or <see langword="null"/> for VISA's
    /// defaults; <see cref="SerialSettings.Default"/> is stored as
    /// <see langword="null"/>, so the defaults have one representation.
    /// </summary>
    public SerialSettings? Serial
    {
        get;
        init => field = WithoutDefaults(value);
    } = WithoutDefaults(Serial);

    /// <summary>Whether the device is on a serial line, that is, has an ASRL resource.</summary>
    public bool IsSerial => Resource is VisaResource.AsrlBoard or VisaResource.AsrlPort;

    /// <summary>
    /// Whether the device sets a terminator that has no effect: HiSLIP,
    /// VXI-11, USBTMC and GPIB end messages themselves, so only SOCKET and
    /// serial devices use one (ADR 0055 §5).
    /// </summary>
    public bool IgnoresTermination =>
        (ReadTermination is not null || WriteTermination is not null)
        && Resource
            is not (VisaResource.TcpipSocket or VisaResource.AsrlBoard or VisaResource.AsrlPort);

    private static SerialSettings? WithoutDefaults(SerialSettings? settings) =>
        settings == SerialSettings.Default ? null : settings;
}
