namespace IviCli.Domain.Protocols;

/// <summary>
/// The USBTMC-USB488 profile of ADR 0049 §2 as a
/// <see cref="UsbDeviceDefinition"/>: one configuration, one interface
/// carrying the class triple the inbox class driver binds against, and
/// the bulk-OUT / bulk-IN / interrupt-IN endpoint trio the subclass
/// requires.
///
/// Everything the profile fixes is a constant here and everything an
/// operator chooses is a parameter, so the descriptors a host reads can
/// be traced back to one named decision each. The sizes are those of a
/// <strong>high-speed</strong> device and have to stay coherent with the
/// speed the device block reports over USB/IP: bulk endpoints at 512
/// bytes and <c>bMaxPacketSize0</c> at 64 are the only values USB 2.0
/// §5.8.3 and §5.5.3 allow at that speed.
/// </summary>
public static class UsbTmcDeviceProfile
{
    /// <summary>The one interface, alternate setting 0.</summary>
    public const byte InterfaceNumber = 0;

    /// <summary>
    /// <c>bDeviceClass</c> zero: the class lives on the interface, which
    /// is what makes a host bind its USBTMC driver per interface rather
    /// than per device.
    /// </summary>
    public const byte DeviceClassPerInterface = 0x00;

    /// <summary><c>bDeviceSubClass</c>, unused when the class is per interface.</summary>
    public const byte DeviceSubClassPerInterface = 0x00;

    /// <summary><c>bDeviceProtocol</c>, unused when the class is per interface.</summary>
    public const byte DeviceProtocolPerInterface = 0x00;

    /// <summary>Bulk-OUT: endpoint 1, host to device.</summary>
    public const byte BulkOutEndpointAddress = 0x01;

    /// <summary>Bulk-IN: endpoint 1, device to host.</summary>
    public const byte BulkInEndpointAddress = 0x81;

    /// <summary>Interrupt-IN: endpoint 2, device to host — the USB488 SRQ path.</summary>
    public const byte InterruptInEndpointAddress = 0x82;

    /// <summary>
    /// <c>wMaxPacketSize</c> of both bulk endpoints. A high-speed bulk
    /// endpoint has no other legal value.
    /// </summary>
    public const ushort BulkMaxPacketSize = 512;

    /// <summary>
    /// <c>wMaxPacketSize</c> of the interrupt endpoint. A USB488 service
    /// request notification is two bytes, so the smallest packet the
    /// descriptor can name comfortably holds one.
    /// </summary>
    public const ushort InterruptMaxPacketSize = 8;

    /// <summary><c>bInterval</c> of a bulk endpoint: unused, hence zero.</summary>
    public const byte BulkInterval = 0;

    /// <summary>
    /// <c>bInterval</c> of the interrupt endpoint. Service requests are
    /// rare events, so the host is asked to poll slowly rather than every
    /// microframe.
    /// </summary>
    public const byte InterruptInterval = 16;

    /// <summary>The value SET_CONFIGURATION selects; never zero.</summary>
    public const byte ConfigurationValue = 1;

    /// <summary>
    /// <c>bMaxPower</c> in milliamps. An emulated device draws nothing,
    /// but the field has to name a plausible budget.
    /// </summary>
    public const ushort MaxPowerMilliamps = 100;

    /// <summary>
    /// A device that exists only as software takes no power from a bus
    /// that does not exist either, so it declares itself self-powered —
    /// in the configuration descriptor and in GET_STATUS alike.
    /// </summary>
    public const bool SelfPowered = true;

    /// <summary>
    /// Builds the device definition for one mock instrument.
    /// </summary>
    /// <param name="idVendor">
    /// <c>idVendor</c>, the first field of the <c>USB0::…::INSTR</c>
    /// resource name a VISA builds from this device.
    /// </param>
    /// <param name="idProduct"><c>idProduct</c>, the second field of that name.</param>
    /// <param name="bcdDevice">Device release number in BCD.</param>
    /// <param name="manufacturer">The manufacturer string descriptor.</param>
    /// <param name="product">The product string descriptor.</param>
    /// <param name="serialNumber">
    /// The serial number string descriptor, and the last field of the
    /// resource name.
    /// </param>
    public static UsbDeviceDefinition Create(
        ushort idVendor,
        ushort idProduct,
        ushort bcdDevice,
        string manufacturer,
        string product,
        string serialNumber
    ) =>
        new(
            IdVendor: idVendor,
            IdProduct: idProduct,
            BcdDevice: bcdDevice,
            DeviceClass: DeviceClassPerInterface,
            DeviceSubClass: DeviceSubClassPerInterface,
            DeviceProtocol: DeviceProtocolPerInterface,
            Manufacturer: manufacturer,
            Product: product,
            SerialNumber: serialNumber,
            SelfPowered: SelfPowered,
            Configuration: new UsbConfigurationDefinition(
                ConfigurationValue: ConfigurationValue,
                MaxPowerMilliamps: MaxPowerMilliamps,
                Interfaces:
                [
                    new UsbInterfaceDefinition(
                        InterfaceNumber: InterfaceNumber,
                        InterfaceClass: UsbTmcConstants.InterfaceClass,
                        InterfaceSubClass: UsbTmcConstants.InterfaceSubClass,
                        InterfaceProtocol: UsbTmcConstants.InterfaceProtocolUsb488,
                        Endpoints:
                        [
                            new UsbEndpointDefinition(
                                Address: BulkOutEndpointAddress,
                                TransferType: UsbEndpointTransferType.Bulk,
                                MaxPacketSize: BulkMaxPacketSize,
                                Interval: BulkInterval
                            ),
                            new UsbEndpointDefinition(
                                Address: BulkInEndpointAddress,
                                TransferType: UsbEndpointTransferType.Bulk,
                                MaxPacketSize: BulkMaxPacketSize,
                                Interval: BulkInterval
                            ),
                            new UsbEndpointDefinition(
                                Address: InterruptInEndpointAddress,
                                TransferType: UsbEndpointTransferType.Interrupt,
                                MaxPacketSize: InterruptMaxPacketSize,
                                Interval: InterruptInterval
                            ),
                        ]
                    ),
                ]
            )
        );
}
