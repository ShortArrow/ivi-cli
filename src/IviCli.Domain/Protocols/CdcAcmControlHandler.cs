namespace IviCli.Domain.Protocols;

/// <summary>
/// The CDC-ACM class control requests of ADR 0049 §5, stacked on the seam
/// <see cref="UsbControlPipe"/> leaves open: the pipe answers the
/// standard requests of USB 2.0 §9.4 and returns
/// <see cref="UsbControlOutcome.NotHandled"/> for everything class-typed,
/// which is what this handler takes.
///
/// It answers exactly the three requests the ACM functional descriptor
/// claims (<see cref="CdcAcmConstants.AcmCapabilityLineCoding"/>):
/// SET_LINE_CODING, GET_LINE_CODING and SET_CONTROL_LINE_STATE, all of
/// PSTN 1.1 §6.3, all addressed to the communications interface. A class
/// request the profile does not implement stalls rather than being
/// answered with something plausible, because a host that hears "yes" to
/// SEND_BREAK will believe a break was sent.
///
/// Every piece of state here is state the host round-trips. There is no
/// UART under this device, so a coding of 9600 7-E-2 moves bytes exactly
/// as 115200 8-N-1 does, and the control lines drive nothing.
/// </summary>
public sealed class CdcAcmControlHandler
{
    private CdcLineCoding _lineCoding = CdcLineCoding.Default;

    /// <summary>
    /// The coding the host last set, or <see cref="CdcLineCoding.Default"/>
    /// until it sets one.
    /// </summary>
    public CdcLineCoding LineCoding => _lineCoding;

    /// <summary>
    /// DTR as the host last drove it (PSTN 1.1 §6.3.12). A terminal
    /// raises it on opening the port and drops it on closing, so it is
    /// the closest thing this profile has to a session boundary.
    /// </summary>
    public bool DataTerminalReady { get; private set; }

    /// <summary>RTS as the host last drove it. Nothing here acts on it.</summary>
    public bool RequestToSend { get; private set; }

    /// <summary>
    /// Answers one control transfer.
    ///
    /// Standard and vendor requests come back
    /// <see cref="UsbControlOutcome.NotHandled"/> so this handler can sit
    /// in a stack without swallowing what belongs to another layer; a
    /// class request this device does not implement, or one addressed to
    /// anything but the communications interface, stalls.
    /// </summary>
    /// <param name="setup">The SETUP packet that opened the transfer.</param>
    /// <param name="outPayload">
    /// The OUT data stage, empty when the transfer has none. Only
    /// SET_LINE_CODING carries one.
    /// </param>
    public UsbControlResult Handle(UsbSetupPacket setup, ReadOnlyMemory<byte> outPayload)
    {
        if (setup.Type != UsbRequestType.Class)
        {
            return UsbControlResult.NotHandled();
        }

        if (
            setup.Recipient != UsbRecipient.Interface
            || setup.WIndex != CdcAcmDeviceProfile.CommunicationsInterfaceNumber
        )
        {
            return UsbControlResult.Stall();
        }

        return setup.BRequest switch
        {
            CdcAcmConstants.RequestSetLineCoding => SetLineCoding(outPayload),
            CdcAcmConstants.RequestGetLineCoding => Truncate(_lineCoding.ToArray(), setup.WLength),
            CdcAcmConstants.RequestSetControlLineState => SetControlLineState(setup),
            _ => UsbControlResult.Stall(),
        };
    }

    /// <summary>
    /// SET_LINE_CODING (PSTN 1.1 §6.3.10): the coding arrives in the OUT
    /// data stage. A data stage of any other length is a malformed
    /// request and stalls, leaving the coding the host set before it.
    /// </summary>
    private UsbControlResult SetLineCoding(ReadOnlyMemory<byte> outPayload)
    {
        if (outPayload.Length != CdcLineCoding.Size)
        {
            return UsbControlResult.Stall();
        }

        _lineCoding = CdcLineCoding.Read(outPayload.Span);
        return UsbControlResult.HandledEmpty();
    }

    /// <summary>
    /// SET_CONTROL_LINE_STATE (PSTN 1.1 §6.3.12): <c>wValue</c> carries
    /// DTR in bit 0 and RTS in bit 1, and the request has no data stage.
    /// </summary>
    private UsbControlResult SetControlLineState(UsbSetupPacket setup)
    {
        DataTerminalReady = (setup.WValue & CdcAcmConstants.ControlLineStateDtr) != 0;
        RequestToSend = (setup.WValue & CdcAcmConstants.ControlLineStateRts) != 0;
        return UsbControlResult.HandledEmpty();
    }

    /// <summary>
    /// Cuts a response to the bytes the host asked for, the same way
    /// endpoint 0 truncates a descriptor: a short <c>wLength</c> is a
    /// short answer, not an error.
    /// </summary>
    private static UsbControlResult Truncate(byte[] response, ushort wLength) =>
        UsbControlResult.Handled(response.Length <= wLength ? response : response[..wLength]);
}
