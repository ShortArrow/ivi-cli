namespace IviCli.Domain.Protocols;

/// <summary>What one bulk-OUT transfer did to the exchange.</summary>
public enum UsbTmcBulkOutOutcome
{
    /// <summary>
    /// A DEV_DEP_MSG_OUT without EOM: its data joined the message being
    /// assembled and nothing is complete yet.
    /// </summary>
    Accumulated = 0,

    /// <summary>
    /// A DEV_DEP_MSG_OUT with EOM finished a message.
    /// <see cref="UsbTmcBulkOutResult.Message"/> carries it.
    /// </summary>
    MessageComplete = 1,

    /// <summary>
    /// A REQUEST_DEV_DEP_MSG_IN: the host is waiting for an answer, which
    /// <see cref="UsbTmcMessagePump.SupplyResponse"/> provides and
    /// <see cref="UsbTmcMessagePump.TryTakeBulkIn"/> hands back.
    /// </summary>
    InRequested = 2,

    /// <summary>
    /// The transfer breaks the framing or the <c>bTag</c> discipline. The
    /// endpoint stalls and the exchange keeps whatever state it had.
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// A USB488 TRIGGER: the host asked the device to trigger. It carries
    /// no message and leaves the exchange as it found it, so what the
    /// caller does with it is a backend call, not an answer to assemble.
    /// </summary>
    TriggerRequested = 4,
}

/// <summary>
/// One complete host-to-device message: the bytes exactly as they
/// arrived, no line ending stripped and nothing interpreted.
/// <paramref name="BTag"/> is the tag of the transfer that
/// <em>opened</em> the message, which is the one the host used to name
/// it even when continuation transfers carried their own.
/// </summary>
public readonly record struct UsbTmcOutboundMessage(byte BTag, byte[] Content);

/// <summary>
/// The outcome of one bulk-OUT transfer. <see cref="Message"/> is set
/// only for <see cref="UsbTmcBulkOutOutcome.MessageComplete"/> and
/// <see cref="Reason"/> only for <see cref="UsbTmcBulkOutOutcome.Rejected"/>.
/// </summary>
public readonly record struct UsbTmcBulkOutResult(
    UsbTmcBulkOutOutcome Outcome,
    UsbTmcOutboundMessage? Message,
    string? Reason
)
{
    /// <summary>Data joined the message under assembly.</summary>
    public static UsbTmcBulkOutResult Accumulated() =>
        new(UsbTmcBulkOutOutcome.Accumulated, null, null);

    /// <summary>A message finished.</summary>
    public static UsbTmcBulkOutResult Complete(UsbTmcOutboundMessage message) =>
        new(UsbTmcBulkOutOutcome.MessageComplete, message, null);

    /// <summary>The host asked for an answer.</summary>
    public static UsbTmcBulkOutResult InRequested() =>
        new(UsbTmcBulkOutOutcome.InRequested, null, null);

    /// <summary>The host asked the device to trigger.</summary>
    public static UsbTmcBulkOutResult TriggerRequested() =>
        new(UsbTmcBulkOutOutcome.TriggerRequested, null, null);

    /// <summary>The transfer is not one this device can act on.</summary>
    public static UsbTmcBulkOutResult Rejected(string reason) =>
        new(UsbTmcBulkOutOutcome.Rejected, null, reason);
}

/// <summary>
/// The Bulk-IN transfer the host is waiting for: the <c>bTag</c> the
/// answer must echo and the most it may carry.
/// </summary>
public readonly record struct UsbTmcInRequest(
    byte BTag,
    uint MaxTransferSize,
    bool TermCharEnabled,
    byte TermChar
);

/// <summary>
/// The USBTMC message exchange with no transport under it: bulk
/// transfers in, complete messages and bulk transfers out, and the small
/// amount of state USBTMC 1.00 §3.2-§3.3 puts between them — the message
/// being assembled, the <c>bTag</c> that may not repeat, and the answer
/// the host has asked for but not yet been given.
///
/// Deliberately free of SCPI, of asynchrony and of I/O. Phase 3b's
/// server loop is what turns URBs into these calls and the returned
/// records back into URBs; what a message <em>means</em> belongs to the
/// scenario engine (ADR 0049 §3), further up still.
/// </summary>
public sealed class UsbTmcMessagePump
{
    private readonly List<byte> _accumulated = [];
    private byte _accumulatingBTag;
    private byte _previousBTag;
    private UsbTmcInRequest? _pendingIn;
    private byte[] _response = [];
    private int _responseOffset;
    private bool _responseSupplied;

    /// <summary>
    /// The Bulk-IN transfer the host is waiting for, or null when it is
    /// waiting for none. Stays set between the transfers of a split
    /// answer, since those are still that request's data.
    /// </summary>
    public UsbTmcInRequest? PendingIn => _pendingIn;

    /// <summary>
    /// True while message data has arrived that no EOM has closed yet.
    /// </summary>
    public bool IsAccumulating => _accumulated.Count > 0 || _accumulatingBTag != NoBTag;

    /// <summary>
    /// Feeds one bulk-OUT transfer through the exchange.
    ///
    /// A malformed header — broken <c>bTagInverse</c>, reserved
    /// <c>bTag</c>, a <c>TransferSize</c> the transfer does not carry —
    /// is reported as <see cref="UsbTmcBulkOutOutcome.Rejected"/> rather
    /// than thrown, because the caller's answer to it is a stalled
    /// endpoint, not an error path.
    /// </summary>
    public UsbTmcBulkOutResult SubmitBulkOut(ReadOnlySpan<byte> transfer)
    {
        byte msgId;
        try
        {
            msgId = UsbTmcCodec.ReadMsgId(transfer);
        }
        catch (InvalidDataException failure)
        {
            return UsbTmcBulkOutResult.Rejected(failure.Message);
        }

        return msgId switch
        {
            UsbTmcConstants.MsgIdDevDepMsgOut => AcceptMessageData(transfer),
            UsbTmcConstants.MsgIdRequestDevDepMsgIn => AcceptInRequest(transfer),
            UsbTmcConstants.MsgIdTrigger => AcceptTrigger(transfer),
            _ => UsbTmcBulkOutResult.Rejected(
                $"USBTMC MsgID {msgId} is not part of the device profile"
            ),
        };
    }

    /// <summary>
    /// Hands the pump the bytes that answer the host's request — in
    /// Phase 3b, whatever the backend produced. May be called before the
    /// request arrives; the answer then waits for it.
    /// </summary>
    public void SupplyResponse(ReadOnlySpan<byte> response)
    {
        _response = response.ToArray();
        _responseOffset = 0;
        _responseSupplied = true;
    }

    /// <summary>
    /// Builds the next DEV_DEP_MSG_IN transfer, at most
    /// <see cref="UsbTmcInRequest.MaxTransferSize"/> bytes of it, with
    /// EOM set only once the answer runs out. Returns false when the host
    /// has not asked or the answer is not ready.
    /// </summary>
    public bool TryTakeBulkIn(out byte[] transfer)
    {
        transfer = [];
        if (_pendingIn is not { } request || !_responseSupplied)
        {
            return false;
        }

        var remaining = _response.Length - _responseOffset;
        var chunk = (int)Math.Min((uint)remaining, request.MaxTransferSize);
        var endOfMessage = chunk == remaining;

        transfer = UsbTmcCodec.WriteDevDepMsgIn(
            new UsbTmcDevDepMsgIn(
                BTag: request.BTag,
                EndOfMessage: endOfMessage,
                Payload: _response.AsSpan(_responseOffset, chunk).ToArray()
            )
        );
        _responseOffset += chunk;

        if (endOfMessage)
        {
            DiscardResponse();
        }

        return true;
    }

    /// <summary>
    /// Returns the exchange to its just-enumerated state, which is what
    /// INITIATE_CLEAR asks for (USBTMC 1.00 §4.2.1.6): the half-assembled
    /// message is dropped, the pending answer with it, and the
    /// <c>bTag</c> history forgotten so a host that restarts its counter
    /// at 1 is not refused.
    /// </summary>
    public void Clear()
    {
        _accumulated.Clear();
        _accumulatingBTag = NoBTag;
        _previousBTag = NoBTag;
        DiscardResponse();
    }

    private UsbTmcBulkOutResult AcceptMessageData(ReadOnlySpan<byte> transfer)
    {
        UsbTmcDevDepMsgOut message;
        try
        {
            message = UsbTmcCodec.ReadDevDepMsgOut(transfer);
        }
        catch (InvalidDataException failure)
        {
            return UsbTmcBulkOutResult.Rejected(failure.Message);
        }

        if (RepeatsPreviousBTag(message.BTag, out var rejection))
        {
            return rejection;
        }

        if (_accumulatingBTag == NoBTag)
        {
            _accumulatingBTag = message.BTag;
        }

        _accumulated.AddRange(message.Payload);

        if (!message.EndOfMessage)
        {
            return UsbTmcBulkOutResult.Accumulated();
        }

        var complete = new UsbTmcOutboundMessage(_accumulatingBTag, [.. _accumulated]);
        _accumulated.Clear();
        _accumulatingBTag = NoBTag;
        return UsbTmcBulkOutResult.Complete(complete);
    }

    private UsbTmcBulkOutResult AcceptInRequest(ReadOnlySpan<byte> transfer)
    {
        UsbTmcRequestDevDepMsgIn request;
        try
        {
            request = UsbTmcCodec.ReadRequestDevDepMsgIn(transfer);
        }
        catch (InvalidDataException failure)
        {
            return UsbTmcBulkOutResult.Rejected(failure.Message);
        }

        if (RepeatsPreviousBTag(request.BTag, out var rejection))
        {
            return rejection;
        }

        if (request.TransferSize == 0)
        {
            return UsbTmcBulkOutResult.Rejected(
                "REQUEST_DEV_DEP_MSG_IN asked for a TransferSize of 0 bytes"
            );
        }

        _pendingIn = new UsbTmcInRequest(
            BTag: request.BTag,
            MaxTransferSize: request.TransferSize,
            TermCharEnabled: request.TermCharEnabled,
            TermChar: request.TermChar
        );
        return UsbTmcBulkOutResult.InRequested();
    }

    /// <summary>
    /// USB488 1.00 §3.2.2: a TRIGGER is a header on its own. It touches
    /// none of the exchange's state — a message half assembled when one
    /// arrives goes on being assembled — beyond the <c>bTag</c> every
    /// Bulk-OUT header takes part in.
    /// </summary>
    private UsbTmcBulkOutResult AcceptTrigger(ReadOnlySpan<byte> transfer)
    {
        UsbTmcTrigger trigger;
        try
        {
            trigger = UsbTmcCodec.ReadTrigger(transfer);
        }
        catch (InvalidDataException failure)
        {
            return UsbTmcBulkOutResult.Rejected(failure.Message);
        }

        return RepeatsPreviousBTag(trigger.BTag, out var rejection)
            ? rejection
            : UsbTmcBulkOutResult.TriggerRequested();
    }

    /// <summary>
    /// USBTMC 1.00 §3.2.1.2: consecutive Bulk-OUT headers must not carry
    /// the same <c>bTag</c>, which is how a device tells a retransmission
    /// from a new transfer. Only a rejection updates nothing; an accepted
    /// tag becomes the one the next transfer is measured against.
    /// </summary>
    private bool RepeatsPreviousBTag(byte bTag, out UsbTmcBulkOutResult rejection)
    {
        if (bTag == _previousBTag)
        {
            rejection = UsbTmcBulkOutResult.Rejected(
                $"bTag 0x{bTag:X2} repeats the previous Bulk-OUT header"
            );
            return true;
        }

        _previousBTag = bTag;
        rejection = default;
        return false;
    }

    private void DiscardResponse()
    {
        _pendingIn = null;
        _response = [];
        _responseOffset = 0;
        _responseSupplied = false;
    }

    /// <summary>
    /// Stands in for "no header seen yet": zero is reserved as a
    /// <c>bTag</c>, so no transfer can collide with it.
    /// </summary>
    private const byte NoBTag = 0;
}
