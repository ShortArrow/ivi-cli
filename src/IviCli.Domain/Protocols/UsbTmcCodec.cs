using System.Buffers.Binary;

namespace IviCli.Domain.Protocols;

/// <summary>
/// The USBTMC bulk framing of ADR 0049 §2: the 12-byte headers of
/// USBTMC 1.00 §3.2 and the message data behind them, decoded from and
/// encoded to one bulk transfer.
///
/// Every multi-byte field is <strong>little endian</strong>, the byte
/// order USB fixes for its own structures and the opposite of the
/// big-endian USB/IP header that carries these bytes
/// (<see cref="UsbIpCodec"/>). Reads are strict: a header whose
/// <c>bTagInverse</c> is not the complement of its <c>bTag</c>, or whose
/// <c>TransferSize</c> claims data the transfer does not carry, is a
/// broken transfer rather than a message with odd fields.
/// </summary>
public static class UsbTmcCodec
{
    /// <summary>
    /// <c>MsgID</c> of a transfer, read without decoding anything else —
    /// what a dispatcher needs to pick the right reader.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The transfer is shorter than a header.
    /// </exception>
    public static byte ReadMsgId(ReadOnlySpan<byte> transfer)
    {
        EnsureHeaderPresent(transfer);
        return transfer[MsgIdOffset];
    }

    /// <summary>Decodes one DEV_DEP_MSG_OUT transfer.</summary>
    /// <exception cref="InvalidDataException">The transfer is malformed.</exception>
    public static UsbTmcDevDepMsgOut ReadDevDepMsgOut(ReadOnlySpan<byte> transfer)
    {
        var bTag = ReadHeader(transfer, UsbTmcConstants.MsgIdDevDepMsgOut);
        return new UsbTmcDevDepMsgOut(
            BTag: bTag,
            EndOfMessage: HasAttribute(transfer, UsbTmcConstants.TransferAttributeEndOfMessage),
            Payload: ReadPayload(transfer)
        );
    }

    /// <summary>Decodes one REQUEST_DEV_DEP_MSG_IN transfer.</summary>
    /// <exception cref="InvalidDataException">The transfer is malformed.</exception>
    public static UsbTmcRequestDevDepMsgIn ReadRequestDevDepMsgIn(ReadOnlySpan<byte> transfer)
    {
        var bTag = ReadHeader(transfer, UsbTmcConstants.MsgIdRequestDevDepMsgIn);
        return new UsbTmcRequestDevDepMsgIn(
            BTag: bTag,
            TransferSize: ReadTransferSize(transfer),
            TermCharEnabled: HasAttribute(
                transfer,
                UsbTmcConstants.TransferAttributeTermCharEnabled
            ),
            TermChar: transfer[TermCharOffset]
        );
    }

    /// <summary>Decodes one USB488 TRIGGER transfer.</summary>
    /// <exception cref="InvalidDataException">The transfer is malformed.</exception>
    public static UsbTmcTrigger ReadTrigger(ReadOnlySpan<byte> transfer) =>
        new(BTag: ReadHeader(transfer, UsbTmcConstants.MsgIdTrigger));

    /// <summary>
    /// Encodes one USB488 TRIGGER transfer: a header whose whole tail is
    /// reserved, hence twelve zero-padded bytes and no message data
    /// (USB488 1.00 §3.2.2).
    /// </summary>
    public static byte[] WriteTrigger(UsbTmcTrigger trigger)
    {
        var transfer = new byte[UsbTmcConstants.BulkHeaderSize];
        WriteHeader(transfer, UsbTmcConstants.MsgIdTrigger, trigger.BTag);
        return transfer;
    }

    /// <summary>
    /// Decodes one DEV_DEP_MSG_IN transfer — the direction the device
    /// writes, read back by tests and by anything replaying a capture.
    /// </summary>
    /// <exception cref="InvalidDataException">The transfer is malformed.</exception>
    public static UsbTmcDevDepMsgIn ReadDevDepMsgIn(ReadOnlySpan<byte> transfer)
    {
        var bTag = ReadHeader(transfer, UsbTmcConstants.MsgIdDevDepMsgIn);
        return new UsbTmcDevDepMsgIn(
            BTag: bTag,
            EndOfMessage: HasAttribute(transfer, UsbTmcConstants.TransferAttributeEndOfMessage),
            Payload: ReadPayload(transfer)
        );
    }

    /// <summary>
    /// Encodes one DEV_DEP_MSG_OUT transfer: header, message data, and
    /// the padding that takes the whole transfer to a
    /// <see cref="UsbTmcConstants.PayloadAlignment"/> boundary.
    /// </summary>
    public static byte[] WriteDevDepMsgOut(UsbTmcDevDepMsgOut message) =>
        WriteMessageTransfer(
            UsbTmcConstants.MsgIdDevDepMsgOut,
            message.BTag,
            message.EndOfMessage,
            message.Payload
        );

    /// <summary>Encodes one DEV_DEP_MSG_IN transfer.</summary>
    public static byte[] WriteDevDepMsgIn(UsbTmcDevDepMsgIn message) =>
        WriteMessageTransfer(
            UsbTmcConstants.MsgIdDevDepMsgIn,
            message.BTag,
            message.EndOfMessage,
            message.Payload
        );

    /// <summary>
    /// Encodes one REQUEST_DEV_DEP_MSG_IN transfer, which is a header and
    /// nothing else, so no padding arises.
    /// </summary>
    public static byte[] WriteRequestDevDepMsgIn(UsbTmcRequestDevDepMsgIn request)
    {
        var transfer = new byte[UsbTmcConstants.BulkHeaderSize];
        WriteHeader(transfer, UsbTmcConstants.MsgIdRequestDevDepMsgIn, request.BTag);
        BinaryPrimitives.WriteUInt32LittleEndian(
            transfer.AsSpan(TransferSizeOffset, TransferSizeLength),
            request.TransferSize
        );
        transfer[AttributesOffset] = request.TermCharEnabled
            ? UsbTmcConstants.TransferAttributeTermCharEnabled
            : NoAttributes;
        transfer[TermCharOffset] = request.TermChar;
        return transfer;
    }

    private static byte[] WriteMessageTransfer(
        byte msgId,
        byte bTag,
        bool endOfMessage,
        byte[] payload
    )
    {
        ArgumentNullException.ThrowIfNull(payload);

        var transfer = new byte[
            UsbTmcConstants.BulkHeaderSize + UsbTmcConstants.AlignedPayloadLength(payload.Length)
        ];
        WriteHeader(transfer, msgId, bTag);
        BinaryPrimitives.WriteUInt32LittleEndian(
            transfer.AsSpan(TransferSizeOffset, TransferSizeLength),
            (uint)payload.Length
        );
        transfer[AttributesOffset] = endOfMessage
            ? UsbTmcConstants.TransferAttributeEndOfMessage
            : NoAttributes;
        payload.CopyTo(transfer.AsSpan(UsbTmcConstants.BulkHeaderSize));
        return transfer;
    }

    /// <summary>
    /// Writes the four bytes every bulk header opens with. The
    /// complement of <c>bTag</c> is derived here rather than supplied, so
    /// a caller cannot emit the inconsistency <see cref="ReadHeader"/>
    /// rejects.
    /// </summary>
    private static void WriteHeader(Span<byte> transfer, byte msgId, byte bTag)
    {
        if (bTag < UsbTmcConstants.MinimumBTag)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bTag),
                bTag,
                $"bTag {bTag} is reserved; USBTMC bulk headers start at {UsbTmcConstants.MinimumBTag}"
            );
        }

        transfer[MsgIdOffset] = msgId;
        transfer[BTagOffset] = bTag;
        transfer[BTagInverseOffset] = (byte)~bTag;
        transfer[ReservedOffset] = 0x00;
    }

    /// <summary>
    /// Validates the common header prefix and returns its <c>bTag</c>.
    /// </summary>
    private static byte ReadHeader(ReadOnlySpan<byte> transfer, byte expectedMsgId)
    {
        EnsureHeaderPresent(transfer);

        if (transfer[MsgIdOffset] != expectedMsgId)
        {
            throw new InvalidDataException(
                $"USBTMC MsgID mismatch (expected {expectedMsgId}, got {transfer[MsgIdOffset]})"
            );
        }

        var bTag = transfer[BTagOffset];
        if (bTag < UsbTmcConstants.MinimumBTag)
        {
            throw new InvalidDataException("USBTMC bTag 0 is reserved");
        }

        var inverse = transfer[BTagInverseOffset];
        if (inverse != (byte)~bTag)
        {
            throw new InvalidDataException(
                $"USBTMC bTagInverse mismatch (bTag 0x{bTag:X2} needs 0x{(byte)~bTag:X2}, got 0x{inverse:X2})"
            );
        }

        return bTag;
    }

    private static void EnsureHeaderPresent(ReadOnlySpan<byte> transfer)
    {
        if (transfer.Length < UsbTmcConstants.BulkHeaderSize)
        {
            throw new InvalidDataException(
                $"USBTMC bulk header needs {UsbTmcConstants.BulkHeaderSize} bytes, transfer holds {transfer.Length}"
            );
        }
    }

    private static uint ReadTransferSize(ReadOnlySpan<byte> transfer) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            transfer.Slice(TransferSizeOffset, TransferSizeLength)
        );

    private static bool HasAttribute(ReadOnlySpan<byte> transfer, byte attribute) =>
        (transfer[AttributesOffset] & attribute) != 0;

    /// <summary>
    /// The message data behind the header. Trailing alignment padding is
    /// on the wire but outside <c>TransferSize</c>, so it is dropped
    /// here.
    /// </summary>
    private static byte[] ReadPayload(ReadOnlySpan<byte> transfer)
    {
        var transferSize = ReadTransferSize(transfer);
        var available = (uint)(transfer.Length - UsbTmcConstants.BulkHeaderSize);
        if (transferSize > available)
        {
            throw new InvalidDataException(
                $"USBTMC TransferSize {transferSize} exceeds the {available} message bytes the transfer carries"
            );
        }

        return transfer.Slice(UsbTmcConstants.BulkHeaderSize, (int)transferSize).ToArray();
    }

    private const int MsgIdOffset = 0;
    private const int BTagOffset = 1;
    private const int BTagInverseOffset = 2;
    private const int ReservedOffset = 3;
    private const int TransferSizeOffset = 4;
    private const int TransferSizeLength = 4;
    private const int AttributesOffset = 8;
    private const int TermCharOffset = 9;
    private const byte NoAttributes = 0x00;
}
