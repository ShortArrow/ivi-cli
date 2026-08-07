namespace IviCli.Domain.Protocols;

/// <summary>What endpoint 0 did with a control transfer.</summary>
public enum UsbControlOutcome
{
    /// <summary>
    /// The request was answered. <see cref="UsbControlResult.Data"/>
    /// carries the IN data stage, already truncated to <c>wLength</c>,
    /// and is empty when the request has no data stage.
    /// </summary>
    Handled = 0,

    /// <summary>
    /// The request is one this device recognises but must refuse — an
    /// unknown descriptor, an unsupported standard request. The host
    /// sees a stalled endpoint.
    /// </summary>
    Stall = 1,

    /// <summary>
    /// The request is not this layer's to answer. Class and vendor
    /// requests land here so a class layer above (USBTMC/USB488, ADR
    /// 0049 §2) can take them; nothing above means the transfer stalls.
    /// </summary>
    NotHandled = 2,
}

/// <summary>
/// The outcome of one control transfer. <see cref="Data"/> is empty
/// unless <see cref="Outcome"/> is <see cref="UsbControlOutcome.Handled"/>.
/// </summary>
public readonly record struct UsbControlResult(UsbControlOutcome Outcome, byte[] Data)
{
    /// <summary>The request was answered with <paramref name="data"/>.</summary>
    public static UsbControlResult Handled(byte[] data) => new(UsbControlOutcome.Handled, data);

    /// <summary>The request was answered with an empty data stage.</summary>
    public static UsbControlResult HandledEmpty() => new(UsbControlOutcome.Handled, []);

    /// <summary>The endpoint stalls.</summary>
    public static UsbControlResult Stall() => new(UsbControlOutcome.Stall, []);

    /// <summary>The request is left to a layer above.</summary>
    public static UsbControlResult NotHandled() => new(UsbControlOutcome.NotHandled, []);
}

/// <summary>
/// The endpoint-0 state machine of one emulated device: the standard
/// device requests of USB 2.0 §9.4 answered from a
/// <see cref="UsbDeviceDefinition"/>, plus the single piece of state
/// those requests own — the selected configuration.
///
/// Class-agnostic by construction. Class and vendor requests return
/// <see cref="UsbControlOutcome.NotHandled"/> rather than an answer or a
/// stall, which is the seam a USBTMC/USB488 layer plugs into later.
/// </summary>
public sealed class UsbControlPipe
{
    /// <summary>
    /// USBIP_RET_SUBMIT status for a stalled endpoint: <c>-EPIPE</c>,
    /// the errno the Linux USB core reports for a protocol stall and the
    /// value the USB/IP client expects to see.
    /// </summary>
    public const int EndpointStalledStatus = -32;

    private readonly UsbDeviceDefinition _definition;

    /// <summary>Binds the pipe to the device it answers for.</summary>
    public UsbControlPipe(UsbDeviceDefinition definition)
    {
        _definition = definition;
    }

    /// <summary>
    /// The configuration SET_CONFIGURATION selected, zero until the host
    /// selects one — the "not configured" state of USB 2.0 §9.4.7.
    /// </summary>
    public byte ConfigurationValue { get; private set; }

    /// <summary>Handles a control transfer that has no OUT data stage.</summary>
    public UsbControlResult Handle(UsbSetupPacket setup) =>
        Handle(setup, ReadOnlyMemory<byte>.Empty);

    /// <summary>
    /// Handles one control transfer.
    /// </summary>
    /// <param name="setup">The SETUP packet that opened the transfer.</param>
    /// <param name="outPayload">
    /// The OUT data stage, empty when the transfer has none. No standard
    /// request this pipe implements carries one; the parameter exists so
    /// the signature does not change when a class layer that needs it
    /// arrives.
    /// </param>
    public UsbControlResult Handle(UsbSetupPacket setup, ReadOnlyMemory<byte> outPayload)
    {
        if (setup.Type != UsbRequestType.Standard)
        {
            return UsbControlResult.NotHandled();
        }

        return setup.BRequest switch
        {
            UsbStandardRequest.GetDescriptor => GetDescriptor(setup),
            UsbStandardRequest.SetConfiguration => SetConfiguration(setup),
            UsbStandardRequest.GetConfiguration => GetConfiguration(setup),
            UsbStandardRequest.GetStatus => GetStatus(setup),
            UsbStandardRequest.SetAddress => UsbControlResult.HandledEmpty(),
            _ => UsbControlResult.Stall(),
        };
    }

    /// <summary>
    /// Answers a USBIP_CMD_SUBMIT addressed to endpoint 0 — the seam
    /// where the USB/IP tunnel (big-endian headers) hands over to USB
    /// proper (little-endian SETUP and descriptors).
    ///
    /// A handled transfer completes with status 0 and
    /// <c>actual_length</c> equal to the bytes that moved; a stalled or
    /// unhandled one with <see cref="EndpointStalledStatus"/> and no
    /// data. The reply header echoes the request's <c>seqnum</c> and
    /// zeroes <c>devid</c>, <c>direction</c> and <c>ep</c>, as the
    /// protocol requires of every server-side message.
    /// </summary>
    public (UsbIpRetSubmit Reply, byte[] Payload) HandleEp0(
        UsbIpCmdSubmit submit,
        ReadOnlyMemory<byte> outPayload
    ) => HandleEp0(submit, outPayload, NoClassLayer);

    /// <summary>
    /// Answers a USBIP_CMD_SUBMIT addressed to endpoint 0 with a class
    /// layer behind the standard requests.
    ///
    /// The standard state machine runs first; only what it leaves
    /// <see cref="UsbControlOutcome.NotHandled"/> — every class and vendor
    /// request — reaches <paramref name="classFallback"/>. A fallback that
    /// declines in turn leaves the transfer stalled, so the two-layer
    /// composition has exactly the completion semantics the single-layer
    /// overload has.
    /// </summary>
    /// <param name="submit">The URB the host submitted to endpoint 0.</param>
    /// <param name="outPayload">The OUT data stage, empty when there is none.</param>
    /// <param name="classFallback">
    /// The class layer — <see cref="UsbTmcControlHandler.Handle"/> for the
    /// USBTMC-USB488 profile, whose every request is decided by the setup
    /// packet alone.
    /// </param>
    public (UsbIpRetSubmit Reply, byte[] Payload) HandleEp0(
        UsbIpCmdSubmit submit,
        ReadOnlyMemory<byte> outPayload,
        Func<UsbSetupPacket, UsbControlResult> classFallback
    )
    {
        ArgumentNullException.ThrowIfNull(classFallback);

        return HandleEp0(submit, outPayload, (setup, _) => classFallback(setup));
    }

    /// <summary>
    /// Answers a USBIP_CMD_SUBMIT addressed to endpoint 0 with a class
    /// layer that reads the OUT data stage as well as the setup packet —
    /// SET_LINE_CODING of the CDC-ACM profile (ADR 0049 §5) is the first
    /// request whose meaning lives there.
    ///
    /// The standard state machine runs first; only what it leaves
    /// <see cref="UsbControlOutcome.NotHandled"/> — every class and vendor
    /// request — reaches <paramref name="classFallback"/>. A fallback that
    /// declines in turn leaves the transfer stalled.
    ///
    /// <c>actual_length</c> reports the bytes that moved on the data
    /// stage, which the direction bit of the setup packet decides: the
    /// returned payload for an IN transfer, the accepted OUT payload for
    /// an OUT one, and zero whenever the transfer stalls.
    /// </summary>
    /// <param name="submit">The URB the host submitted to endpoint 0.</param>
    /// <param name="outPayload">The OUT data stage, empty when there is none.</param>
    /// <param name="classFallback">
    /// The class layer — <see cref="CdcAcmControlHandler.Handle"/> for the
    /// CDC-ACM profile.
    /// </param>
    public (UsbIpRetSubmit Reply, byte[] Payload) HandleEp0(
        UsbIpCmdSubmit submit,
        ReadOnlyMemory<byte> outPayload,
        Func<UsbSetupPacket, ReadOnlyMemory<byte>, UsbControlResult> classFallback
    )
    {
        ArgumentNullException.ThrowIfNull(classFallback);

        var setup = UsbSetupPacket.Read(submit.Setup);
        var result = Handle(setup, outPayload);
        if (result.Outcome == UsbControlOutcome.NotHandled)
        {
            result = classFallback(setup, outPayload);
        }

        var handled = result.Outcome == UsbControlOutcome.Handled;
        var payload = handled ? result.Data : [];

        var reply = new UsbIpRetSubmit(
            Header: new UsbIpHeaderBasic(
                Command: UsbIpConstants.RetSubmit,
                SeqNum: submit.Header.SeqNum,
                DevId: 0,
                Direction: 0,
                Ep: 0
            ),
            Status: handled ? 0 : EndpointStalledStatus,
            ActualLength: handled ? TransferredLength(setup, outPayload, payload) : 0,
            StartFrame: 0,
            NumberOfPackets: UsbIpConstants.NumberOfPacketsNonIso,
            ErrorCount: 0
        );

        return (reply, payload);
    }

    /// <summary>
    /// The data-stage bytes a completed transfer moved. An OUT transfer
    /// moves the host's payload — the device answers it with a status
    /// stage, not with data — so reporting the response length there
    /// would tell the host nothing arrived.
    /// </summary>
    private static int TransferredLength(
        UsbSetupPacket setup,
        ReadOnlyMemory<byte> outPayload,
        byte[] payload
    ) => setup.Direction == UsbTransferDirection.HostToDevice ? outPayload.Length : payload.Length;

    /// <summary>
    /// GET_DESCRIPTOR. Only the types endpoint 0 serves standalone are
    /// answered: interface and endpoint descriptors are reachable inside
    /// the configuration hierarchy only, so asking for one directly
    /// stalls, as does an index the device has no descriptor for.
    /// </summary>
    private UsbControlResult GetDescriptor(UsbSetupPacket setup)
    {
        switch (setup.DescriptorType)
        {
            case UsbDescriptorType.Device:
                return setup.DescriptorIndex == 0
                    ? Truncate(UsbDescriptors.BuildDeviceDescriptor(_definition), setup.WLength)
                    : UsbControlResult.Stall();

            case UsbDescriptorType.Configuration:
                // One configuration, so index 0 is the only one; the
                // host reads 9 bytes to learn wTotalLength and then the
                // whole blob, and both are plain truncations.
                return setup.DescriptorIndex == 0
                    ? Truncate(UsbDescriptors.BuildConfigurationBlob(_definition), setup.WLength)
                    : UsbControlResult.Stall();

            case UsbDescriptorType.String:
                return UsbDescriptors.TryBuildStringDescriptor(
                    _definition,
                    setup.DescriptorIndex,
                    out var descriptor
                )
                    ? Truncate(descriptor, setup.WLength)
                    : UsbControlResult.Stall();

            default:
                return UsbControlResult.Stall();
        }
    }

    /// <summary>
    /// SET_CONFIGURATION. The low byte of <c>wValue</c> is either the
    /// device's own configuration value or zero, which unconfigures it;
    /// anything else stalls.
    /// </summary>
    private UsbControlResult SetConfiguration(UsbSetupPacket setup)
    {
        var requested = (byte)(setup.WValue & 0xFF);
        if (requested != 0 && requested != _definition.Configuration.ConfigurationValue)
        {
            return UsbControlResult.Stall();
        }

        ConfigurationValue = requested;
        return UsbControlResult.HandledEmpty();
    }

    /// <summary>GET_CONFIGURATION: one byte, zero before SET_CONFIGURATION.</summary>
    private UsbControlResult GetConfiguration(UsbSetupPacket setup) =>
        Truncate([ConfigurationValue], setup.WLength);

    /// <summary>
    /// GET_STATUS on the device: two little-endian bytes whose bit 0 is
    /// self-powered and bit 1 remote wakeup, which the mock never
    /// enables. Interface and endpoint recipients are not modelled and
    /// stall.
    /// </summary>
    private UsbControlResult GetStatus(UsbSetupPacket setup)
    {
        if (setup.Recipient != UsbRecipient.Device)
        {
            return UsbControlResult.Stall();
        }

        var status = (byte)(_definition.SelfPowered ? SelfPoweredStatusBit : 0);
        return Truncate([status, 0x00], setup.WLength);
    }

    /// <summary>
    /// Cuts a descriptor to the bytes the host asked for. A host that
    /// asks for fewer bytes than the descriptor holds gets a short
    /// answer, not an error — that is how the two-stage configuration
    /// read works.
    /// </summary>
    private static UsbControlResult Truncate(byte[] data, ushort wLength) =>
        UsbControlResult.Handled(data.Length <= wLength ? data : data[..wLength]);

    /// <summary>
    /// The class layer of a device that has none: everything the standard
    /// requests declined stays declined, and therefore stalls.
    /// </summary>
    private static UsbControlResult NoClassLayer(UsbSetupPacket setup) =>
        UsbControlResult.NotHandled();

    private const byte SelfPoweredStatusBit = 0x01;
}
