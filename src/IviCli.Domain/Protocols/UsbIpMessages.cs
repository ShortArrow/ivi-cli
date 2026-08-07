namespace IviCli.Domain.Protocols;

/// <summary>
/// The 0x138-byte device block shared by OP_REP_DEVLIST and
/// OP_REP_IMPORT. <see cref="Path"/> occupies 256 bytes and
/// <see cref="BusId"/> 32 bytes on the wire, both NUL-terminated and
/// zero-filled; the remaining fields are fixed-width big-endian
/// integers.
/// </summary>
public readonly record struct UsbIpDeviceInfo(
    string Path,
    string BusId,
    uint BusNum,
    uint DevNum,
    uint Speed,
    ushort IdVendor,
    ushort IdProduct,
    ushort BcdDevice,
    byte DeviceClass,
    byte DeviceSubClass,
    byte DeviceProtocol,
    byte ConfigurationValue,
    byte NumConfigurations,
    byte NumInterfaces
);

/// <summary>
/// One interface tuple appended by OP_REP_DEVLIST, repeated
/// <c>bNumInterfaces</c> times. The fourth wire byte is an alignment
/// pad the spec fixes at zero, so it is written and skipped by the
/// codec rather than modelled as data.
/// </summary>
public readonly record struct UsbIpInterfaceInfo(
    byte InterfaceClass,
    byte InterfaceSubClass,
    byte InterfaceProtocol
);

/// <summary>
/// One exported device as OP_REP_DEVLIST describes it: the device block
/// followed by its interface tuples.
/// </summary>
public readonly record struct UsbIpExportedDevice(
    UsbIpDeviceInfo Device,
    UsbIpInterfaceInfo[] Interfaces
);

/// <summary>
/// OP_REQ_DEVLIST. The trailing status word is unused and fixed at
/// zero, so it is not modelled.
/// </summary>
public readonly record struct OpReqDevlist(ushort Version);

/// <summary>OP_REP_DEVLIST: the exported devices, or none.</summary>
public readonly record struct OpRepDevlist(
    ushort Version,
    uint Status,
    UsbIpExportedDevice[] Devices
);

/// <summary>
/// OP_REQ_IMPORT. The status word is unused and fixed at zero;
/// <see cref="BusId"/> occupies 32 NUL-padded bytes on the wire.
/// </summary>
public readonly record struct OpReqImport(ushort Version, string BusId);

/// <summary>
/// OP_REP_IMPORT. The device block is present only when
/// <see cref="Status"/> is <see cref="UsbIpConstants.StatusOk"/>;
/// otherwise the reply ends with the status field.
/// </summary>
public readonly record struct OpRepImport(ushort Version, uint Status, UsbIpDeviceInfo? Device);

/// <summary>
/// <c>usbip_header_basic</c>, the 20 bytes every command message opens
/// with. <see cref="DevId"/> is <c>(busnum &lt;&lt; 16) | devnum</c> on
/// requests; <see cref="DevId"/>, <see cref="Direction"/> and
/// <see cref="Ep"/> are all zero on server replies.
/// </summary>
public readonly record struct UsbIpHeaderBasic(
    uint Command,
    uint SeqNum,
    uint DevId,
    uint Direction,
    uint Ep
);

/// <summary>
/// The 48-byte USBIP_CMD_SUBMIT header. Any transfer buffer follows the
/// header on the wire and is not part of this record — see
/// <see cref="UsbIpCodec.CmdSubmitPayloadLength"/> for its length.
/// </summary>
public readonly record struct UsbIpCmdSubmit(
    UsbIpHeaderBasic Header,
    uint TransferFlags,
    int TransferBufferLength,
    int StartFrame,
    int NumberOfPackets,
    int Interval,
    byte[] Setup
);

/// <summary>
/// The 48-byte USBIP_RET_SUBMIT header. <see cref="Status"/> is zero on
/// success and a negative errno otherwise; the trailing 8 padding bytes
/// are fixed at zero and not modelled. Any transfer buffer follows the
/// header — see <see cref="UsbIpCodec.RetSubmitPayloadLength"/>.
/// </summary>
public readonly record struct UsbIpRetSubmit(
    UsbIpHeaderBasic Header,
    int Status,
    int ActualLength,
    int StartFrame,
    int NumberOfPackets,
    int ErrorCount
);

/// <summary>
/// USBIP_CMD_UNLINK. <see cref="UnlinkSeqNum"/> names the
/// USBIP_CMD_SUBMIT to cancel; the trailing 24 padding bytes are fixed
/// at zero and not modelled.
/// </summary>
public readonly record struct UsbIpCmdUnlink(UsbIpHeaderBasic Header, uint UnlinkSeqNum);

/// <summary>
/// USBIP_RET_UNLINK. <see cref="Status"/> is a negative errno when the
/// URB was actually unlinked; the trailing 24 padding bytes are fixed
/// at zero and not modelled.
/// </summary>
public readonly record struct UsbIpRetUnlink(UsbIpHeaderBasic Header, int Status);
