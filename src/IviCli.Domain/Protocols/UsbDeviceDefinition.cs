namespace IviCli.Domain.Protocols;

/// <summary>
/// Transfer type of an endpoint, <c>bmAttributes</c> D1..D0 of the
/// endpoint descriptor (USB 2.0 §9.6.6).
/// </summary>
public enum UsbEndpointTransferType
{
    /// <summary>Control — endpoint 0 only, never described by a descriptor.</summary>
    Control = 0,

    /// <summary>Isochronous. Out of scope for the mock device (ADR 0049 §6).</summary>
    Isochronous = 1,

    /// <summary>Bulk — the USBTMC message endpoints.</summary>
    Bulk = 2,

    /// <summary>Interrupt — the USB488 service-request notification endpoint.</summary>
    Interrupt = 3,
}

/// <summary>
/// One endpoint of an interface. <paramref name="Address"/> is the
/// complete <c>bEndpointAddress</c>: the endpoint number in D3..D0 and
/// the direction in D7, so bulk IN endpoint 1 is <c>0x81</c> and bulk
/// OUT endpoint 1 is <c>0x01</c>. <paramref name="Interval"/> is the
/// polling interval bulk endpoints leave at zero.
/// </summary>
public readonly record struct UsbEndpointDefinition(
    byte Address,
    UsbEndpointTransferType TransferType,
    ushort MaxPacketSize,
    byte Interval
)
{
    /// <summary>Direction bit (D7) of <c>bEndpointAddress</c>; set means IN.</summary>
    public const byte DirectionIn = 0x80;

    /// <summary>Endpoint number, <c>bEndpointAddress</c> D3..D0.</summary>
    public byte Number => (byte)(Address & 0x0F);

    /// <summary>Direction the endpoint carries data in.</summary>
    public UsbTransferDirection Direction =>
        (Address & DirectionIn) != 0
            ? UsbTransferDirection.DeviceToHost
            : UsbTransferDirection.HostToDevice;
}

/// <summary>
/// One interface of a configuration: the class triple a host driver
/// binds against, plus the endpoints it owns. Alternate settings are out
/// of scope, so every interface is alternate setting 0.
/// </summary>
public sealed record UsbInterfaceDefinition(
    byte InterfaceNumber,
    byte InterfaceClass,
    byte InterfaceSubClass,
    byte InterfaceProtocol,
    IReadOnlyList<UsbEndpointDefinition> Endpoints
);

/// <summary>
/// The single configuration the mock device offers.
/// <paramref name="ConfigurationValue"/> is what SET_CONFIGURATION
/// selects and GET_CONFIGURATION echoes; it is never zero, which the
/// specification reserves for "not configured".
/// <paramref name="MaxPowerMilliamps"/> is stated in milliamps and
/// encoded into the descriptor's 2 mA units.
/// </summary>
public sealed record UsbConfigurationDefinition(
    byte ConfigurationValue,
    ushort MaxPowerMilliamps,
    IReadOnlyList<UsbInterfaceDefinition> Interfaces
);

/// <summary>
/// A class-agnostic description of one emulated USB device — everything
/// the standard descriptors and the standard device requests need, and
/// nothing about USBTMC, CDC-ACM or any other profile. Phase 3
/// instantiates it for the USBTMC-USB488 profile of ADR 0049 §2; the
/// descriptor builders and the endpoint-0 pipe read it and only it.
/// </summary>
/// <param name="IdVendor">USB vendor ID, <c>idVendor</c>.</param>
/// <param name="IdProduct">USB product ID, <c>idProduct</c>.</param>
/// <param name="BcdDevice">Device release number in BCD, <c>bcdDevice</c>.</param>
/// <param name="DeviceClass">
/// <c>bDeviceClass</c>. Zero means the class is declared per interface,
/// which is what a USBTMC device does.
/// </param>
/// <param name="DeviceSubClass"><c>bDeviceSubClass</c>.</param>
/// <param name="DeviceProtocol"><c>bDeviceProtocol</c>.</param>
/// <param name="Manufacturer">String served at <see cref="UsbDescriptors.ManufacturerStringIndex"/>.</param>
/// <param name="Product">String served at <see cref="UsbDescriptors.ProductStringIndex"/>.</param>
/// <param name="SerialNumber">
/// String served at <see cref="UsbDescriptors.SerialNumberStringIndex"/>,
/// and the last field of the <c>USB0::…::INSTR</c> resource name a VISA
/// builds from this device.
/// </param>
/// <param name="SelfPowered">
/// One source of truth for two places the specification asks the same
/// question: <c>bmAttributes</c> D6 of the configuration descriptor and
/// bit 0 of the GET_STATUS device response.
/// </param>
/// <param name="Configuration">The one configuration this device offers.</param>
public sealed record UsbDeviceDefinition(
    ushort IdVendor,
    ushort IdProduct,
    ushort BcdDevice,
    byte DeviceClass,
    byte DeviceSubClass,
    byte DeviceProtocol,
    string Manufacturer,
    string Product,
    string SerialNumber,
    bool SelfPowered,
    UsbConfigurationDefinition Configuration
)
{
    /// <summary>
    /// <c>bMaxPacketSize0</c>, the endpoint-0 packet size. 64 is the only
    /// value a high-speed device may report (USB 2.0 §5.5.3); the
    /// property stays settable so a full-speed profile can drop to 8, 16
    /// or 32.
    /// </summary>
    public byte MaxPacketSize0 { get; init; } = 64;
}
