using System.Buffers.Binary;

namespace IviCli.Domain.Protocols;

/// <summary>
/// The USBTMC class control requests of ADR 0049 §2, stacked on the seam
/// <see cref="UsbControlPipe"/> leaves open: the pipe answers the
/// standard requests of USB 2.0 §9.4 and returns
/// <see cref="UsbControlOutcome.NotHandled"/> for everything class-typed,
/// which is what this handler takes.
///
/// The layout of each response is USBTMC 1.00 §4.2 and USB488 1.00 §4.3,
/// little endian throughout. Recipients follow the specification rather
/// than a single convention: the four abort requests address an
/// <em>endpoint</em> (<c>wIndex</c> names it), while clear, capabilities,
/// indicator pulse and the USB488 requests address the interface.
/// </summary>
public sealed class UsbTmcControlHandler
{
    private readonly UsbTmcMessagePump _pump;
    private readonly Usb488Notifier _notifier;

    /// <summary>
    /// Binds the handler to the exchange INITIATE_CLEAR resets and to the
    /// service-request state READ_STATUS_BYTE polls.
    /// </summary>
    public UsbTmcControlHandler(UsbTmcMessagePump pump, Usb488Notifier notifier)
    {
        _pump = pump;
        _notifier = notifier;
    }

    /// <summary>
    /// Answers one control transfer.
    ///
    /// Standard and vendor requests come back
    /// <see cref="UsbControlOutcome.NotHandled"/> so this handler can sit
    /// in a stack without swallowing what belongs to another layer; a
    /// class request this device does not implement, or one addressed to
    /// the wrong recipient, stalls.
    /// </summary>
    public UsbControlResult Handle(UsbSetupPacket setup)
    {
        if (setup.Type != UsbRequestType.Class)
        {
            return UsbControlResult.NotHandled();
        }

        return setup.Recipient switch
        {
            UsbRecipient.Interface => HandleInterfaceRequest(setup),
            UsbRecipient.Endpoint => HandleEndpointRequest(setup),
            _ => UsbControlResult.Stall(),
        };
    }

    /// <summary>
    /// The requests <c>wIndex</c> addresses to the interface. Dispatching
    /// by recipient before request code is what keeps INITIATE_CLEAR from
    /// clearing anything when the host addressed it wrongly.
    /// </summary>
    private UsbControlResult HandleInterfaceRequest(UsbSetupPacket setup) =>
        setup.BRequest switch
        {
            UsbTmcConstants.RequestInitiateClear => Truncate(InitiateClear(), setup.WLength),
            UsbTmcConstants.RequestCheckClearStatus => Truncate(
                CheckClearStatusResponse(),
                setup.WLength
            ),
            UsbTmcConstants.RequestGetCapabilities => Truncate(
                CapabilitiesResponse(),
                setup.WLength
            ),
            UsbTmcConstants.Request488ReadStatusByte => ReadStatusByte(setup),
            // The capabilities deny the indicator pulse, so answering one
            // would contradict what the device just told the host.
            UsbTmcConstants.RequestIndicatorPulse => UsbControlResult.Stall(),
            // RL0 for the same reason: the mock has no front panel to take
            // remote or local, so it declares no RL1 and refuses the three
            // requests that capability would have brought with it.
            UsbTmcConstants.Request488RenControl => UsbControlResult.Stall(),
            UsbTmcConstants.Request488GoToLocal => UsbControlResult.Stall(),
            UsbTmcConstants.Request488LocalLockout => UsbControlResult.Stall(),
            _ => UsbControlResult.Stall(),
        };

    /// <summary>
    /// The four abort requests, which <c>wIndex</c> addresses to a bulk
    /// endpoint (USBTMC 1.00 §4.2.1.2).
    /// </summary>
    private static UsbControlResult HandleEndpointRequest(UsbSetupPacket setup) =>
        setup.BRequest switch
        {
            UsbTmcConstants.RequestInitiateAbortBulkOut => Truncate(
                InitiateAbortResponse(setup),
                setup.WLength
            ),
            UsbTmcConstants.RequestCheckAbortBulkOutStatus => Truncate(
                CheckAbortBulkOutStatusResponse(),
                setup.WLength
            ),
            UsbTmcConstants.RequestInitiateAbortBulkIn => Truncate(
                InitiateAbortResponse(setup),
                setup.WLength
            ),
            UsbTmcConstants.RequestCheckAbortBulkInStatus => Truncate(
                CheckAbortBulkInStatusResponse(),
                setup.WLength
            ),
            _ => UsbControlResult.Stall(),
        };

    /// <summary>
    /// READ_STATUS_BYTE (USB488 1.00 §4.3.1): the serial poll. The device
    /// answers three bytes and queues the matching notification for the
    /// interrupt-IN endpoint, which is where a host that claimed that
    /// endpoint reads the status from.
    ///
    /// <c>wValue</c> carries the <c>bTag</c>, and one outside the range
    /// the notification format can carry stalls rather than being
    /// silently corrected — the stance this handler takes on every
    /// malformed request.
    /// </summary>
    private UsbControlResult ReadStatusByte(UsbSetupPacket setup) =>
        Usb488Notifier.IsReadableStatusByteTag(setup.WValue)
            ? Truncate(_notifier.ReadStatusByte((byte)setup.WValue), setup.WLength)
            : UsbControlResult.Stall();

    /// <summary>
    /// The GET_CAPABILITIES response of USBTMC 1.00 §4.2.1.8 with the
    /// USB488 subsection of USB488 1.00 §4.2.1.8 appended: 24 bytes
    /// describing an interface that is neither talk-only nor listen-only,
    /// has no indicator pulse, and cannot end a Bulk-IN on a termination
    /// character.
    /// </summary>
    private static byte[] CapabilitiesResponse()
    {
        var response = new byte[UsbTmcConstants.CapabilitiesResponseSize];
        response[StatusOffset] = UsbTmcConstants.StatusSuccess;
        BinaryPrimitives.WriteUInt16LittleEndian(
            response.AsSpan(BcdUsbTmcOffset, BcdLength),
            UsbTmcConstants.BcdUsbTmc
        );
        response[InterfaceCapabilitiesOffset] = NoCapabilities;
        response[DeviceCapabilitiesOffset] = NoCapabilities;
        BinaryPrimitives.WriteUInt16LittleEndian(
            response.AsSpan(BcdUsb488Offset, BcdLength),
            UsbTmcConstants.BcdUsb488
        );
        response[Interface488CapabilitiesOffset] = UsbTmcConstants.Interface488CapabilityTrigger;

        // SR1 and DT1 are what this device implements and no more: the
        // interrupt-IN endpoint carries service requests and the TRIGGER
        // message reaches the backend. RL0 stays clear because nothing
        // here has a front panel to take, and the 488.2 and SCPI bits
        // because neither compliance is claimed.
        response[Device488CapabilitiesOffset] =
            UsbTmcConstants.Device488CapabilitySr1 | UsbTmcConstants.Device488CapabilityDt1;

        return response;
    }

    /// <summary>
    /// INITIATE_CLEAR: the device discards what it was assembling and
    /// what it was about to send, then reports success. USBTMC 1.00
    /// §4.2.1.6.
    /// </summary>
    private byte[] InitiateClear()
    {
        _pump.Clear();
        return [UsbTmcConstants.StatusSuccess];
    }

    /// <summary>
    /// CHECK_CLEAR_STATUS: the clear finished inside INITIATE_CLEAR, so
    /// it is never pending, and <c>bmClear</c> bit 0 stays clear because
    /// no Bulk-IN data is queued.
    /// </summary>
    private static byte[] CheckClearStatusResponse() =>
        [UsbTmcConstants.StatusSuccess, NoQueuedBulkInData];

    /// <summary>
    /// INITIATE_ABORT_BULK_OUT and INITIATE_ABORT_BULK_IN. This device
    /// completes every transfer within the call that submits it, so there
    /// is never one in flight to abort; the <c>bTag</c> the host named in
    /// <c>wValue</c> is echoed as the second byte.
    /// </summary>
    private static byte[] InitiateAbortResponse(UsbSetupPacket setup) =>
        [UsbTmcConstants.StatusTransferNotInProgress, (byte)(setup.WValue & ByteMask)];

    /// <summary>
    /// CHECK_ABORT_BULK_OUT_STATUS: no INITIATE_ABORT_BULK_OUT was ever
    /// accepted, so no split transaction is running and no bytes were
    /// received under one.
    /// </summary>
    private static byte[] CheckAbortBulkOutStatusResponse()
    {
        var response = new byte[AbortStatusResponseSize];
        response[StatusOffset] = UsbTmcConstants.StatusSplitNotInProgress;
        BinaryPrimitives.WriteUInt32LittleEndian(
            response.AsSpan(AbortByteCountOffset, AbortByteCountLength),
            NoBytesTransferred
        );
        return response;
    }

    /// <summary>
    /// CHECK_ABORT_BULK_IN_STATUS: as above, and <c>bmAbortBulkIn</c> bit
    /// 0 stays clear because no Bulk-IN data is queued.
    /// </summary>
    private static byte[] CheckAbortBulkInStatusResponse()
    {
        var response = new byte[AbortStatusResponseSize];
        response[StatusOffset] = UsbTmcConstants.StatusSplitNotInProgress;
        response[AbortBulkInFlagsOffset] = NoQueuedBulkInData;
        BinaryPrimitives.WriteUInt32LittleEndian(
            response.AsSpan(AbortByteCountOffset, AbortByteCountLength),
            NoBytesTransferred
        );
        return response;
    }

    /// <summary>
    /// Cuts a response to the bytes the host asked for, the same way
    /// endpoint 0 truncates a descriptor: a short <c>wLength</c> is a
    /// short answer, not an error.
    /// </summary>
    private static UsbControlResult Truncate(byte[] response, ushort wLength) =>
        UsbControlResult.Handled(response.Length <= wLength ? response : response[..wLength]);

    private const int StatusOffset = 0;
    private const int BcdUsbTmcOffset = 2;
    private const int BcdLength = 2;
    private const int InterfaceCapabilitiesOffset = 4;
    private const int DeviceCapabilitiesOffset = 5;
    private const int BcdUsb488Offset = 12;
    private const int Interface488CapabilitiesOffset = 14;
    private const int Device488CapabilitiesOffset = 15;
    private const int AbortStatusResponseSize = 8;
    private const int AbortBulkInFlagsOffset = 1;
    private const int AbortByteCountOffset = 4;
    private const int AbortByteCountLength = 4;
    private const uint NoBytesTransferred = 0;
    private const byte NoQueuedBulkInData = 0x00;
    private const byte NoCapabilities = 0x00;
    private const ushort ByteMask = 0x00FF;
}
