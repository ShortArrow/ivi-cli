namespace IviCli.Domain.Protocols;

/// <summary>
/// Wire-level constants for the USB/IP protocol as published by the
/// Linux kernel (https://docs.kernel.org/usb/usbip_protocol.html).
/// Public so the device server (ADR 0049) and any diagnostic tooling
/// compose messages from the same source of truth — the role
/// <see cref="Vxi11Constants"/> plays for VXI-11.
/// </summary>
public static class UsbIpConstants
{
    /// <summary>Protocol version carried in the first two bytes of every op message.</summary>
    public const ushort ProtocolVersion = 0x0111;

    /// <summary>Op code: retrieve the list of exported USB devices (client → server).</summary>
    public const ushort OpReqDevlist = 0x8005;

    /// <summary>Op code: the list of exported USB devices (server → client).</summary>
    public const ushort OpRepDevlist = 0x0005;

    /// <summary>Op code: import (attach) a remote USB device (client → server).</summary>
    public const ushort OpReqImport = 0x8003;

    /// <summary>Op code: reply to an import request (server → client).</summary>
    public const ushort OpRepImport = 0x0003;

    /// <summary>Command code: USBIP_CMD_SUBMIT (submit a URB).</summary>
    public const uint CmdSubmit = 0x0000_0001;

    /// <summary>Command code: USBIP_CMD_UNLINK (cancel a submitted URB).</summary>
    public const uint CmdUnlink = 0x0000_0002;

    /// <summary>Command code: USBIP_RET_SUBMIT (URB completion).</summary>
    public const uint RetSubmit = 0x0000_0003;

    /// <summary>Command code: USBIP_RET_UNLINK (unlink completion).</summary>
    public const uint RetUnlink = 0x0000_0004;

    /// <summary>Direction USBIP_DIR_OUT (host → device).</summary>
    public const uint DirOut = 0;

    /// <summary>Direction USBIP_DIR_IN (device → host).</summary>
    public const uint DirIn = 1;

    /// <summary>Op-message status: OK.</summary>
    public const uint StatusOk = 0;

    /// <summary>Op-message status: error (no device block follows).</summary>
    public const uint StatusError = 1;

    /// <summary>
    /// <c>number_of_packets</c> sentinel for every non-isochronous
    /// transfer. Signed because the field is a 32-bit signed integer on
    /// the wire.
    /// </summary>
    public const int NumberOfPacketsNonIso = -1;

    /// <summary>Well-known TCP port the USB/IP device server listens on.</summary>
    public const int DefaultPort = 3240;

    /// <summary>Size of the op-message preamble (version, code, status).</summary>
    public const int OpHeaderSize = 8;

    /// <summary>Size of the <c>path</c> field in the device block.</summary>
    public const int PathSize = 256;

    /// <summary>Size of the <c>busid</c> field in the device block and in OP_REQ_IMPORT.</summary>
    public const int BusIdSize = 32;

    /// <summary>Size of the device block shared by OP_REP_DEVLIST and OP_REP_IMPORT.</summary>
    public const int DeviceInfoSize = 0x138;

    /// <summary>Size of one interface tuple appended by OP_REP_DEVLIST.</summary>
    public const int InterfaceInfoSize = 4;

    /// <summary>Size of <c>usbip_header_basic</c>.</summary>
    public const int HeaderBasicSize = 20;

    /// <summary>Size of every command header (basic header plus its 28-byte body).</summary>
    public const int CommandHeaderSize = 48;

    /// <summary>Size of the <c>setup</c> field of USBIP_CMD_SUBMIT.</summary>
    public const int SetupSize = 8;

    /// <summary>Speed value USB_SPEED_UNKNOWN.</summary>
    public const uint SpeedUnknown = 0;

    /// <summary>Speed value USB_SPEED_LOW (1.5 Mbit/s).</summary>
    public const uint SpeedLow = 1;

    /// <summary>Speed value USB_SPEED_FULL (12 Mbit/s).</summary>
    public const uint SpeedFull = 2;

    /// <summary>Speed value USB_SPEED_HIGH (480 Mbit/s).</summary>
    public const uint SpeedHigh = 3;

    /// <summary>Speed value USB_SPEED_WIRELESS.</summary>
    public const uint SpeedWireless = 4;

    /// <summary>Speed value USB_SPEED_SUPER (5 Gbit/s).</summary>
    public const uint SpeedSuper = 5;

    /// <summary>Speed value USB_SPEED_SUPER_PLUS (10 Gbit/s).</summary>
    public const uint SpeedSuperPlus = 6;
}
