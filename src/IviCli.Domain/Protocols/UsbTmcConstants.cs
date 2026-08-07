namespace IviCli.Domain.Protocols;

/// <summary>
/// Wire-level constants of the USBTMC 1.00 class specification and its
/// USB488 subclass — the profile ADR 0049 §2 fixes for the emulated
/// instrument. Public so the device server, the codec and any diagnostic
/// tooling compose from one source of truth, the role
/// <see cref="UsbIpConstants"/> plays for the tunnel underneath.
///
/// Every multi-byte field these constants describe is <strong>little
/// endian</strong>, unlike the big-endian USB/IP header that carries it.
/// </summary>
public static class UsbTmcConstants
{
    /// <summary>MsgID of a host-to-device message on the bulk-OUT endpoint.</summary>
    public const byte MsgIdDevDepMsgOut = 1;

    /// <summary>
    /// MsgID of the bulk-OUT header that asks the device to send a
    /// message. Shares its value with <see cref="MsgIdDevDepMsgIn"/>: the
    /// endpoint a header arrives on, not the number, tells the two apart.
    /// </summary>
    public const byte MsgIdRequestDevDepMsgIn = 2;

    /// <summary>MsgID of a device-to-host message on the bulk-IN endpoint.</summary>
    public const byte MsgIdDevDepMsgIn = 2;

    /// <summary>MsgID of a vendor-specific OUT message — out of scope (ADR 0049 §6).</summary>
    public const byte MsgIdVendorSpecificOut = 126;

    /// <summary>MsgID of a vendor-specific IN message — out of scope.</summary>
    public const byte MsgIdVendorSpecificIn = 127;

    /// <summary>MsgID of the USB488 TRIGGER message; Phase 4 territory.</summary>
    public const byte MsgIdTrigger = 128;

    /// <summary>INITIATE_ABORT_BULK_OUT — an endpoint request.</summary>
    public const byte RequestInitiateAbortBulkOut = 1;

    /// <summary>CHECK_ABORT_BULK_OUT_STATUS — an endpoint request.</summary>
    public const byte RequestCheckAbortBulkOutStatus = 2;

    /// <summary>INITIATE_ABORT_BULK_IN — an endpoint request.</summary>
    public const byte RequestInitiateAbortBulkIn = 3;

    /// <summary>CHECK_ABORT_BULK_IN_STATUS — an endpoint request.</summary>
    public const byte RequestCheckAbortBulkInStatus = 4;

    /// <summary>INITIATE_CLEAR — an interface request.</summary>
    public const byte RequestInitiateClear = 5;

    /// <summary>CHECK_CLEAR_STATUS — an interface request.</summary>
    public const byte RequestCheckClearStatus = 6;

    /// <summary>GET_CAPABILITIES — an interface request.</summary>
    public const byte RequestGetCapabilities = 7;

    /// <summary>INDICATOR_PULSE — an interface request, optional by capability.</summary>
    public const byte RequestIndicatorPulse = 64;

    /// <summary>USB488 READ_STATUS_BYTE; arrives with the SRQ path in Phase 4.</summary>
    public const byte Request488ReadStatusByte = 128;

    /// <summary>USB488 REN_CONTROL; Phase 4.</summary>
    public const byte Request488RenControl = 160;

    /// <summary>USB488 GO_TO_LOCAL; Phase 4.</summary>
    public const byte Request488GoToLocal = 161;

    /// <summary>USB488 LOCAL_LOCKOUT; Phase 4.</summary>
    public const byte Request488LocalLockout = 162;

    /// <summary>USBTMC_STATUS_SUCCESS.</summary>
    public const byte StatusSuccess = 0x01;

    /// <summary>USBTMC_STATUS_PENDING: accepted, not finished.</summary>
    public const byte StatusPending = 0x02;

    /// <summary>USBTMC_STATUS_FAILED: the catch-all refusal.</summary>
    public const byte StatusFailed = 0x80;

    /// <summary>
    /// USBTMC_STATUS_TRANSFER_NOT_IN_PROGRESS: an abort was asked for a
    /// transfer the device is not running.
    /// </summary>
    public const byte StatusTransferNotInProgress = 0x81;

    /// <summary>
    /// USBTMC_STATUS_SPLIT_NOT_IN_PROGRESS: a CHECK_… request arrived
    /// without the INITIATE_… that would have started the split
    /// transaction.
    /// </summary>
    public const byte StatusSplitNotInProgress = 0x82;

    /// <summary>USBTMC_STATUS_SPLIT_IN_PROGRESS: another split is already running.</summary>
    public const byte StatusSplitInProgress = 0x83;

    /// <summary>USBTMC version in BCD, the value <c>bcdUSBTMC</c> carries.</summary>
    public const ushort BcdUsbTmc = 0x0100;

    /// <summary>USB488 subclass version in BCD, <c>bcdUSB488</c>.</summary>
    public const ushort BcdUsb488 = 0x0100;

    /// <summary><c>bInterfaceClass</c>: application specific.</summary>
    public const byte InterfaceClass = 0xFE;

    /// <summary><c>bInterfaceSubClass</c>: USBTMC.</summary>
    public const byte InterfaceSubClass = 0x03;

    /// <summary><c>bInterfaceProtocol</c>: the USB488 subclass.</summary>
    public const byte InterfaceProtocolUsb488 = 0x01;

    /// <summary><c>bInterfaceProtocol</c>: USBTMC with no subclass.</summary>
    public const byte InterfaceProtocolNone = 0x00;

    /// <summary>Size of every bulk header, whichever MsgID it carries.</summary>
    public const int BulkHeaderSize = 12;

    /// <summary>
    /// Boundary the message data after a bulk header is padded up to. The
    /// padding is on the wire but outside <c>TransferSize</c>, so a
    /// 6-byte message travels as 12 + 6 + 2 bytes.
    /// </summary>
    public const int PayloadAlignment = 4;

    /// <summary>Size of the GET_CAPABILITIES response, USB488 subsection included.</summary>
    public const int CapabilitiesResponseSize = 24;

    /// <summary>
    /// <c>bmTransferAttributes</c> bit 0 on DEV_DEP_MSG_OUT and
    /// DEV_DEP_MSG_IN: this transfer ends the message.
    /// </summary>
    public const byte TransferAttributeEndOfMessage = 0x01;

    /// <summary>
    /// <c>bmTransferAttributes</c> bit 1 on REQUEST_DEV_DEP_MSG_IN: the
    /// device may end the transfer on <c>TermChar</c>.
    /// </summary>
    public const byte TransferAttributeTermCharEnabled = 0x02;

    /// <summary>
    /// <c>bmTransferAttributes</c> bit 1 on DEV_DEP_MSG_IN: the transfer
    /// did end on <c>TermChar</c>.
    /// </summary>
    public const byte TransferAttributeUsingTermChar = 0x02;

    /// <summary><c>bmIntfcCapabilities</c> bit 0: the interface is listen-only.</summary>
    public const byte InterfaceCapabilityListenOnly = 0x01;

    /// <summary><c>bmIntfcCapabilities</c> bit 1: the interface is talk-only.</summary>
    public const byte InterfaceCapabilityTalkOnly = 0x02;

    /// <summary><c>bmIntfcCapabilities</c> bit 2: INDICATOR_PULSE is supported.</summary>
    public const byte InterfaceCapabilityIndicatorPulse = 0x04;

    /// <summary>
    /// <c>bmDevCapabilities</c> bit 0: the device can end a Bulk-IN
    /// transfer on the requested termination character.
    /// </summary>
    public const byte DeviceCapabilityTermChar = 0x01;

    /// <summary><c>bmIntfcCapabilities488</c> bit 0: the USB488 TRIGGER message.</summary>
    public const byte Interface488CapabilityTrigger = 0x01;

    /// <summary><c>bmIntfcCapabilities488</c> bit 1: REN_CONTROL, GO_TO_LOCAL, LOCAL_LOCKOUT.</summary>
    public const byte Interface488CapabilityRenControl = 0x02;

    /// <summary><c>bmIntfcCapabilities488</c> bit 2: the interface is IEEE 488.2 compliant.</summary>
    public const byte Interface488CapabilityIeee4882 = 0x04;

    /// <summary><c>bmDevCapabilities488</c> bit 0: DT1, device trigger.</summary>
    public const byte Device488CapabilityDt1 = 0x01;

    /// <summary><c>bmDevCapabilities488</c> bit 1: RL1, remote/local.</summary>
    public const byte Device488CapabilityRl1 = 0x02;

    /// <summary>
    /// <c>bmDevCapabilities488</c> bit 2: SR1, the device generates
    /// service requests on the interrupt-IN endpoint. Clear (SR0) until
    /// that endpoint is driven — see
    /// <see cref="UsbTmcControlHandler"/>.
    /// </summary>
    public const byte Device488CapabilitySr1 = 0x04;

    /// <summary><c>bmDevCapabilities488</c> bit 3: the device is SCPI compliant.</summary>
    public const byte Device488CapabilityScpi = 0x08;

    /// <summary>
    /// Lowest legal <c>bTag</c>. Zero is reserved, which is why the Linux
    /// driver steps over it when its counter wraps.
    /// </summary>
    public const byte MinimumBTag = 1;

    /// <summary>
    /// Length of <paramref name="payloadLength"/> bytes of message data
    /// once padded to <see cref="PayloadAlignment"/> — the number of
    /// bytes that follow the header on the wire.
    /// </summary>
    public static int AlignedPayloadLength(int payloadLength) =>
        (payloadLength + PayloadAlignment - 1) & ~(PayloadAlignment - 1);
}
