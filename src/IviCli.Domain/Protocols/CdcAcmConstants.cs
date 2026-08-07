namespace IviCli.Domain.Protocols;

/// <summary>
/// Wire-level constants of the USB Communications Device Class 1.1 and
/// its PSTN Abstract Control Model subclass — the serial-shaped profile
/// ADR 0049 §5 offers beside USBTMC. Public so the device server, the
/// descriptors and any diagnostic tooling compose from one source of
/// truth, the role <see cref="UsbTmcConstants"/> plays for the
/// instrument-shaped profile.
///
/// Every multi-byte field these constants describe is <strong>little
/// endian</strong>, unlike the big-endian USB/IP header that carries it.
/// </summary>
public static class CdcAcmConstants
{
    /// <summary>
    /// <c>bDeviceClass</c> of a communications device (CDC 1.1 §3.1).
    /// Unlike USBTMC, the class is declared at the device as well as at
    /// the interface, because the two interfaces below it are one
    /// function rather than two independent ones.
    /// </summary>
    public const byte CommunicationsDeviceClass = 0x02;

    /// <summary><c>bDeviceSubClass</c>: CDC 1.1 leaves it zero.</summary>
    public const byte DeviceSubClassNone = 0x00;

    /// <summary><c>bDeviceProtocol</c>: CDC 1.1 leaves it zero.</summary>
    public const byte DeviceProtocolNone = 0x00;

    /// <summary><c>bInterfaceClass</c> of the communications interface.</summary>
    public const byte CommunicationsInterfaceClass = 0x02;

    /// <summary>
    /// <c>bInterfaceSubClass</c>: the Abstract Control Model of PSTN 1.1
    /// §3.6, the subclass that models a serial port.
    /// </summary>
    public const byte AbstractControlModelSubClass = 0x02;

    /// <summary>
    /// <c>bInterfaceProtocol</c>: the V.250 (formerly V.25ter) AT command
    /// set, CDC 1.1 table 17. The mock answers no AT command, but the
    /// value is what the host drivers key on: Windows builds the
    /// compatible ID <c>USB\Class_02&amp;SubClass_02&amp;Prot_01</c> from
    /// this triple, and the ones without the protocol field, which is
    /// what the inbox <c>usbser.inf</c> matches (Microsoft, "USB serial
    /// driver (Usbser.sys)", lists <c>USB\Class_02</c> and
    /// <c>USB\Class_02&amp;SubClass_02</c>). The Linux <c>cdc-acm</c>
    /// driver binds the same triple.
    /// </summary>
    public const byte AtCommandProtocolV250 = 0x01;

    /// <summary><c>bInterfaceClass</c> of the data interface, CDC 1.1 §4.5.</summary>
    public const byte DataInterfaceClass = 0x0A;

    /// <summary><c>bInterfaceSubClass</c> of the data interface: reserved, hence zero.</summary>
    public const byte DataInterfaceSubClass = 0x00;

    /// <summary>
    /// <c>bInterfaceProtocol</c> of the data interface: no class-specific
    /// protocol, so the bulk pipes carry a plain byte stream.
    /// </summary>
    public const byte DataInterfaceProtocolNone = 0x00;

    /// <summary>
    /// <c>bDescriptorType</c> of a class-specific interface descriptor,
    /// CDC 1.1 table 24 — the type every functional descriptor carries.
    /// </summary>
    public const byte CsInterfaceDescriptorType = 0x24;

    /// <summary><c>bDescriptorSubtype</c> of the header functional descriptor.</summary>
    public const byte FunctionalDescriptorHeader = 0x00;

    /// <summary><c>bDescriptorSubtype</c> of the call management functional descriptor.</summary>
    public const byte FunctionalDescriptorCallManagement = 0x01;

    /// <summary><c>bDescriptorSubtype</c> of the ACM functional descriptor.</summary>
    public const byte FunctionalDescriptorAbstractControlManagement = 0x02;

    /// <summary><c>bDescriptorSubtype</c> of the union functional descriptor.</summary>
    public const byte FunctionalDescriptorUnion = 0x06;

    /// <summary>CDC version in BCD, the value <c>bcdCDC</c> carries.</summary>
    public const ushort BcdCdc11 = 0x0110;

    /// <summary>
    /// <c>bmCapabilities</c> of the call management functional
    /// descriptor: the device handles no call management and expects none
    /// over the data interface. A mock instrument places no calls.
    /// </summary>
    public const byte CallManagementCapabilityNone = 0x00;

    /// <summary>
    /// <c>bmCapabilities</c> bit 1 of the ACM functional descriptor
    /// (PSTN 1.1 §5.3.2): SET_LINE_CODING, GET_LINE_CODING,
    /// SET_CONTROL_LINE_STATE and the SERIAL_STATE notification. It is
    /// the only bit this device sets, and exactly the set of class
    /// requests the CDC-ACM control handler answers.
    /// </summary>
    public const byte AcmCapabilityLineCoding = 0x02;

    /// <summary>SET_LINE_CODING — a class request addressed to the interface.</summary>
    public const byte RequestSetLineCoding = 0x20;

    /// <summary>GET_LINE_CODING — a class request addressed to the interface.</summary>
    public const byte RequestGetLineCoding = 0x21;

    /// <summary>SET_CONTROL_LINE_STATE — a class request addressed to the interface.</summary>
    public const byte RequestSetControlLineState = 0x22;

    /// <summary>
    /// SEND_BREAK — a class request the ACM capabilities do not claim, so
    /// answering one would contradict what the device told the host.
    /// </summary>
    public const byte RequestSendBreak = 0x23;

    /// <summary>
    /// <c>wValue</c> bit 0 of SET_CONTROL_LINE_STATE: DTR, the host is
    /// present (PSTN 1.1 §6.3.12).
    /// </summary>
    public const ushort ControlLineStateDtr = 0x0001;

    /// <summary><c>wValue</c> bit 1 of SET_CONTROL_LINE_STATE: RTS, carrier control.</summary>
    public const ushort ControlLineStateRts = 0x0002;
}
