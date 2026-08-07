namespace IviCli.Domain.Protocols;

/// <summary>
/// A DEV_DEP_MSG_OUT bulk transfer: message data travelling host to
/// device. <c>TransferSize</c> is not carried as a field because it
/// counts exactly the bytes of <see cref="Payload"/> — the codec derives
/// it on write and checks it on read, so the two can never disagree.
/// <see cref="EndOfMessage"/> is <c>bmTransferAttributes</c> bit 0; a
/// message too large for one transfer arrives as several transfers with
/// the bit set on the last only.
/// </summary>
public readonly record struct UsbTmcDevDepMsgOut(byte BTag, bool EndOfMessage, byte[] Payload)
{
    /// <summary>Bytes of message data this transfer carries, padding excluded.</summary>
    public uint TransferSize => (uint)Payload.Length;
}

/// <summary>
/// A REQUEST_DEV_DEP_MSG_IN bulk transfer: the host asking for a
/// message. It is a header and nothing else.
/// <paramref name="TransferSize"/> is the most the host will accept in
/// the answering transfer, not the size of anything that exists yet.
/// <paramref name="TermChar"/> is meaningless unless
/// <paramref name="TermCharEnabled"/> is set, and a device whose
/// capabilities deny <c>canEndBulkInOnTermChar</c> may ignore both.
/// </summary>
public readonly record struct UsbTmcRequestDevDepMsgIn(
    byte BTag,
    uint TransferSize,
    bool TermCharEnabled,
    byte TermChar
);

/// <summary>
/// A DEV_DEP_MSG_IN bulk transfer: the device's answer.
/// <c>TransferSize</c> counts the bytes in <em>this</em> transfer rather
/// than in the whole message, so a long answer is split across several
/// of these with <see cref="EndOfMessage"/> set on the last only. The
/// <c>bTag</c> is echoed from the REQUEST_DEV_DEP_MSG_IN that asked.
/// </summary>
public readonly record struct UsbTmcDevDepMsgIn(byte BTag, bool EndOfMessage, byte[] Payload)
{
    /// <summary>Bytes of message data this transfer carries, padding excluded.</summary>
    public uint TransferSize => (uint)Payload.Length;
}
