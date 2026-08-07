namespace IviCli.Domain.Protocols;

/// <summary>
/// The CDC-ACM profile of ADR 0049 §5 as a
/// <see cref="UsbDeviceDefinition"/>: one configuration holding the
/// standard pair of interfaces CDC 1.1 §3.3 defines for a communications
/// device — a communications interface carrying the class requests and
/// the notification endpoint, and a data interface carrying the byte
/// stream on a bulk pair.
///
/// The point of the profile is which driver binds. Windows generates the
/// compatible IDs <c>USB\Class_02&amp;SubClass_02&amp;Prot_01</c>,
/// <c>USB\Class_02&amp;SubClass_02</c> and <c>USB\Class_02</c> from the
/// class triple below, and the inbox <c>usbser.inf</c> matches the last
/// two, loading <c>Usbser.sys</c> without any INF of ours (Microsoft,
/// "USB serial driver (Usbser.sys)":
/// <c>learn.microsoft.com/windows-hardware/drivers/usbcon/usb-driver-installation-based-on-compatible-ids</c>).
/// A real COM port then appears, which is the whole reason this profile
/// exists. Linux binds its <c>cdc-acm</c> driver against the same triple
/// and produces <c>/dev/ttyACM*</c>.
///
/// Everything the profile fixes is a constant here and everything an
/// operator chooses is a parameter, so the descriptors a host reads can
/// be traced back to one named decision each. The sizes are those of a
/// <strong>high-speed</strong> device and have to stay coherent with the
/// speed the device block reports over USB/IP: bulk endpoints at 512
/// bytes and <c>bMaxPacketSize0</c> at 64 are the only values USB 2.0
/// §5.8.3 and §5.5.3 allow at that speed.
/// </summary>
public static class CdcAcmDeviceProfile
{
    /// <summary>
    /// The communications interface: the one the class requests address
    /// and the one the union descriptor names as master.
    /// </summary>
    public const byte CommunicationsInterfaceNumber = 0;

    /// <summary>
    /// The data interface: the one the bulk pipes belong to and the one
    /// the union descriptor names as subordinate.
    /// </summary>
    public const byte DataInterfaceNumber = 1;

    /// <summary>Bulk-OUT: endpoint 1, host to device — the host's writes.</summary>
    public const byte BulkOutEndpointAddress = 0x01;

    /// <summary>Bulk-IN: endpoint 1, device to host — the device's answers.</summary>
    public const byte BulkInEndpointAddress = 0x81;

    /// <summary>
    /// Interrupt-IN: endpoint 2, device to host. CDC 1.1 §3.3.1 requires
    /// the communications interface to own a notification endpoint even
    /// when, as here, the device raises no notification of its own.
    /// </summary>
    public const byte InterruptInEndpointAddress = 0x82;

    /// <summary>
    /// <c>wMaxPacketSize</c> of both bulk endpoints. A high-speed bulk
    /// endpoint has no other legal value.
    /// </summary>
    public const ushort BulkMaxPacketSize = 512;

    /// <summary>
    /// <c>wMaxPacketSize</c> of the notification endpoint. The largest
    /// notification the ACM capabilities admit is SERIAL_STATE, an 8-byte
    /// header with a 2-byte payload, and the device sends none, so the
    /// smallest packet the descriptor can name is enough.
    /// </summary>
    public const ushort InterruptMaxPacketSize = 8;

    /// <summary><c>bInterval</c> of a bulk endpoint: unused, hence zero.</summary>
    public const byte BulkInterval = 0;

    /// <summary>
    /// <c>bInterval</c> of the notification endpoint. Nothing is queued
    /// for it, so the host is asked to poll slowly rather than every
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
    /// Builds the device definition for one mock instrument exported as a
    /// serial port.
    /// </summary>
    /// <param name="idVendor"><c>idVendor</c>.</param>
    /// <param name="idProduct"><c>idProduct</c>.</param>
    /// <param name="bcdDevice">Device release number in BCD.</param>
    /// <param name="manufacturer">The manufacturer string descriptor.</param>
    /// <param name="product">The product string descriptor.</param>
    /// <param name="serialNumber">
    /// The serial number string descriptor. Windows keys a COM port
    /// number to it, so two mock devices that share one are two names for
    /// the same port.
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
            DeviceClass: CdcAcmConstants.CommunicationsDeviceClass,
            DeviceSubClass: CdcAcmConstants.DeviceSubClassNone,
            DeviceProtocol: CdcAcmConstants.DeviceProtocolNone,
            Manufacturer: manufacturer,
            Product: product,
            SerialNumber: serialNumber,
            SelfPowered: SelfPowered,
            Configuration: new UsbConfigurationDefinition(
                ConfigurationValue: ConfigurationValue,
                MaxPowerMilliamps: MaxPowerMilliamps,
                Interfaces: [CommunicationsInterface(), DataInterface()]
            )
        );

    /// <summary>
    /// The communications interface: the class triple a serial driver
    /// binds against, the functional descriptors that describe the
    /// function, and the notification endpoint.
    /// </summary>
    private static UsbInterfaceDefinition CommunicationsInterface() =>
        new(
            InterfaceNumber: CommunicationsInterfaceNumber,
            InterfaceClass: CdcAcmConstants.CommunicationsInterfaceClass,
            InterfaceSubClass: CdcAcmConstants.AbstractControlModelSubClass,
            InterfaceProtocol: CdcAcmConstants.AtCommandProtocolV250,
            Endpoints:
            [
                new UsbEndpointDefinition(
                    Address: InterruptInEndpointAddress,
                    TransferType: UsbEndpointTransferType.Interrupt,
                    MaxPacketSize: InterruptMaxPacketSize,
                    Interval: InterruptInterval
                ),
            ]
        )
        {
            ClassSpecificDescriptors =
            [
                HeaderFunctionalDescriptor(),
                CallManagementFunctionalDescriptor(),
                AbstractControlManagementFunctionalDescriptor(),
                UnionFunctionalDescriptor(),
            ],
        };

    /// <summary>
    /// The data interface: no class-specific descriptors, and the bulk
    /// pair the byte stream travels on.
    /// </summary>
    private static UsbInterfaceDefinition DataInterface() =>
        new(
            InterfaceNumber: DataInterfaceNumber,
            InterfaceClass: CdcAcmConstants.DataInterfaceClass,
            InterfaceSubClass: CdcAcmConstants.DataInterfaceSubClass,
            InterfaceProtocol: CdcAcmConstants.DataInterfaceProtocolNone,
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
            ]
        );

    /// <summary>
    /// Header functional descriptor, CDC 1.1 §5.2.3.1: the CDC release
    /// the rest of the descriptors are written against.
    /// </summary>
    private static byte[] HeaderFunctionalDescriptor() =>
        [
            HeaderFunctionalDescriptorLength,
            CdcAcmConstants.CsInterfaceDescriptorType,
            CdcAcmConstants.FunctionalDescriptorHeader,
            (byte)(CdcAcmConstants.BcdCdc11 & 0xFF),
            (byte)(CdcAcmConstants.BcdCdc11 >> 8),
        ];

    /// <summary>
    /// Call management functional descriptor, PSTN 1.1 §5.3.1. The device
    /// manages no calls, and still has to name the data interface the
    /// capability would have used.
    /// </summary>
    private static byte[] CallManagementFunctionalDescriptor() =>
        [
            CallManagementFunctionalDescriptorLength,
            CdcAcmConstants.CsInterfaceDescriptorType,
            CdcAcmConstants.FunctionalDescriptorCallManagement,
            CdcAcmConstants.CallManagementCapabilityNone,
            DataInterfaceNumber,
        ];

    /// <summary>
    /// Abstract control management functional descriptor, PSTN 1.1
    /// §5.3.2: the class requests the device answers, and nothing more.
    /// </summary>
    private static byte[] AbstractControlManagementFunctionalDescriptor() =>
        [
            AbstractControlManagementFunctionalDescriptorLength,
            CdcAcmConstants.CsInterfaceDescriptorType,
            CdcAcmConstants.FunctionalDescriptorAbstractControlManagement,
            CdcAcmConstants.AcmCapabilityLineCoding,
        ];

    /// <summary>
    /// Union functional descriptor, CDC 1.1 §5.2.3.8: which interfaces
    /// form the one function. Without it a host has a control interface
    /// and a data interface and no statement that they belong together.
    /// </summary>
    private static byte[] UnionFunctionalDescriptor() =>
        [
            UnionFunctionalDescriptorLength,
            CdcAcmConstants.CsInterfaceDescriptorType,
            CdcAcmConstants.FunctionalDescriptorUnion,
            CommunicationsInterfaceNumber,
            DataInterfaceNumber,
        ];

    private const byte HeaderFunctionalDescriptorLength = 5;
    private const byte CallManagementFunctionalDescriptorLength = 5;
    private const byte AbstractControlManagementFunctionalDescriptorLength = 4;
    private const byte UnionFunctionalDescriptorLength = 5;
}
