using System.Text;
using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// The bulk transfers every USBTMC golden in this folder is built from.
/// Shared so the codec goldens and the message-pump goldens cannot drift
/// apart, the way <c>UsbGoldenDevice</c> holds the descriptor goldens
/// together.
///
/// Every multi-byte field is <strong>little endian</strong> (USBTMC 1.00
/// §3.2), which is the opposite of the big-endian USB/IP header that
/// carries these bytes.
/// </summary>
internal static class UsbTmcGoldenTransfers
{
    /// <summary>The query the host writes: <c>*IDN?\n</c>, six bytes.</summary>
    internal static byte[] IdnQuery => Encoding.ASCII.GetBytes("*IDN?\n");

    /// <summary>The device's answer: <c>MOCK\n</c>, five bytes.</summary>
    internal static byte[] MockAnswer => Encoding.ASCII.GetBytes("MOCK\n");

    /// <summary>
    /// One complete DEV_DEP_MSG_OUT transfer carrying <c>*IDN?\n</c> with
    /// bTag 1: 12 header bytes, 6 payload bytes, 2 alignment pad bytes.
    /// </summary>
    internal static byte[] DevDepMsgOutIdn =>
        [
            0x01, // MsgID = DEV_DEP_MSG_OUT
            0x01, // bTag = 1
            0xFE, // bTagInverse = ~1
            0x00, // reserved, always zero
            0x06, // TransferSize byte 0 \
            0x00, // TransferSize byte 1  |_ 6, little endian
            0x00, // TransferSize byte 2  |
            0x00, // TransferSize byte 3 /
            0x01, // bmTransferAttributes: bit 0 EOM set
            0x00, // reserved
            0x00, // reserved
            0x00, // reserved
            0x2A, // '*' \
            0x49, // 'I'  |
            0x44, // 'D'  |_ the message, TransferSize bytes of it
            0x4E, // 'N'  |
            0x3F, // '?'  |
            0x0A, // '\n' /
            0x00, // pad to the 4-byte boundary, outside TransferSize
            0x00, // pad
        ];

    /// <summary>
    /// A REQUEST_DEV_DEP_MSG_IN with bTag 2 asking for at most 1024
    /// bytes and no termination character.
    /// </summary>
    internal static byte[] RequestDevDepMsgIn =>
        [
            0x02, // MsgID = REQUEST_DEV_DEP_MSG_IN
            0x02, // bTag = 2
            0xFD, // bTagInverse = ~2
            0x00, // reserved
            0x00, // TransferSize byte 0 \
            0x04, // TransferSize byte 1  |_ 1024, little endian
            0x00, // TransferSize byte 2  |
            0x00, // TransferSize byte 3 /
            0x00, // bmTransferAttributes: bit 1 TermChar enabled, clear
            0x00, // TermChar, meaningless while bit 1 is clear
            0x00, // reserved
            0x00, // reserved
        ];

    /// <summary>
    /// The device's DEV_DEP_MSG_IN answer to
    /// <see cref="RequestDevDepMsgIn"/>: the same bTag, five payload
    /// bytes, EOM, and three pad bytes.
    /// </summary>
    internal static byte[] DevDepMsgInMock =>
        [
            0x02, // MsgID = DEV_DEP_MSG_IN
            0x02, // bTag = 2, echoed from the request
            0xFD, // bTagInverse = ~2
            0x00, // reserved
            0x05, // TransferSize byte 0 \
            0x00, // TransferSize byte 1  |_ 5 bytes in THIS transfer
            0x00, // TransferSize byte 2  |
            0x00, // TransferSize byte 3 /
            0x01, // bmTransferAttributes: bit 0 EOM set
            0x00, // reserved
            0x00, // reserved
            0x00, // reserved
            0x4D, // 'M' \
            0x4F, // 'O'  |
            0x43, // 'C'  |_ the answer
            0x4B, // 'K'  |
            0x0A, // '\n' /
            0x00, // pad \
            0x00, // pad  |_ to the 4-byte boundary
            0x00, // pad /
        ];
}

/// <summary>
/// Golden-vector tests for the USBTMC bulk framing of ADR 0049 §2. Field
/// offsets and widths come from the USBTMC 1.00 bulk headers (§3.2), the
/// same layout tinyusb's <c>usbtmc.h</c> and the Linux <c>usbtmc.c</c>
/// driver encode, so a one-byte drift fails here rather than as an
/// instrument the host class driver cannot talk to.
/// </summary>
public sealed class UsbTmcCodecTests
{
    [Fact]
    public void WriteDevDepMsgOut_emits_the_twenty_byte_golden_transfer()
    {
        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(
                BTag: 1,
                EndOfMessage: true,
                Payload: UsbTmcGoldenTransfers.IdnQuery
            )
        );

        transfer.ShouldBe(UsbTmcGoldenTransfers.DevDepMsgOutIdn);
    }

    [Fact]
    public void WriteDevDepMsgOut_encodes_TransferSize_little_endian()
    {
        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(
                BTag: 1,
                EndOfMessage: true,
                Payload: UsbTmcGoldenTransfers.IdnQuery
            )
        );

        // Six bytes of payload read `06 00 00 00`, not `00 00 00 06`.
        transfer[4..8].ShouldBe([0x06, 0x00, 0x00, 0x00]);
    }

    [Fact]
    public void WriteDevDepMsgOut_derives_bTagInverse_from_bTag()
    {
        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(BTag: 0x2A, EndOfMessage: true, Payload: [0x00])
        );

        transfer[1].ShouldBe((byte)0x2A);
        transfer[2].ShouldBe((byte)0xD5);
    }

    [Theory]
    [InlineData(1, 16)] // 12 + 1, padded by 3
    [InlineData(4, 16)] // 12 + 4, already aligned
    [InlineData(5, 20)] // 12 + 5, padded by 3
    [InlineData(6, 20)] // 12 + 6, padded by 2
    [InlineData(8, 20)] // 12 + 8, already aligned
    public void WriteDevDepMsgOut_pads_the_payload_to_a_four_byte_boundary(
        int payloadLength,
        int expectedTransferLength
    )
    {
        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(BTag: 1, EndOfMessage: true, Payload: new byte[payloadLength])
        );

        transfer.Length.ShouldBe(expectedTransferLength);

        // Padding never counts towards TransferSize.
        transfer[4].ShouldBe((byte)payloadLength);
    }

    [Fact]
    public void WriteDevDepMsgOut_clears_the_EOM_bit_on_a_continuation_transfer()
    {
        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(BTag: 3, EndOfMessage: false, Payload: [0x41, 0x42, 0x43, 0x44])
        );

        transfer[8].ShouldBe((byte)0x00);
    }

    [Fact]
    public void ReadDevDepMsgOut_reads_the_golden_back_into_its_fields()
    {
        var message = UsbTmcCodec.ReadDevDepMsgOut(UsbTmcGoldenTransfers.DevDepMsgOutIdn);

        message.BTag.ShouldBe((byte)1);
        message.EndOfMessage.ShouldBeTrue();
        message.TransferSize.ShouldBe(6u);
        message.Payload.ShouldBe(UsbTmcGoldenTransfers.IdnQuery);
    }

    [Fact]
    public void ReadDevDepMsgOut_rejects_a_bTagInverse_that_is_not_the_complement()
    {
        var transfer = UsbTmcGoldenTransfers.DevDepMsgOutIdn;
        transfer[2] = 0xFF; // ~1 is 0xFE

        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadDevDepMsgOut(transfer));
    }

    [Fact]
    public void ReadDevDepMsgOut_rejects_the_reserved_zero_bTag()
    {
        var transfer = UsbTmcGoldenTransfers.DevDepMsgOutIdn;
        transfer[1] = 0x00;
        transfer[2] = 0xFF;

        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadDevDepMsgOut(transfer));
    }

    [Fact]
    public void ReadDevDepMsgOut_rejects_a_transfer_shorter_than_the_header()
    {
        var transfer = UsbTmcGoldenTransfers.DevDepMsgOutIdn[..11];

        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadDevDepMsgOut(transfer));
    }

    [Fact]
    public void ReadDevDepMsgOut_rejects_a_TransferSize_the_transfer_does_not_carry()
    {
        var transfer = UsbTmcGoldenTransfers.DevDepMsgOutIdn;
        transfer[4] = 0x40; // claims 64 payload bytes, carries 6

        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadDevDepMsgOut(transfer));
    }

    [Fact]
    public void ReadDevDepMsgOut_rejects_a_MsgID_that_is_not_its_own()
    {
        var transfer = UsbTmcGoldenTransfers.RequestDevDepMsgIn;

        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadDevDepMsgOut(transfer));
    }

    [Fact]
    public void WriteRequestDevDepMsgIn_emits_the_twelve_byte_golden()
    {
        var transfer = UsbTmcCodec.WriteRequestDevDepMsgIn(
            new UsbTmcRequestDevDepMsgIn(
                BTag: 2,
                TransferSize: 1024,
                TermCharEnabled: false,
                TermChar: 0
            )
        );

        transfer.ShouldBe(UsbTmcGoldenTransfers.RequestDevDepMsgIn);
        transfer.Length.ShouldBe(UsbTmcConstants.BulkHeaderSize);
    }

    [Fact]
    public void WriteRequestDevDepMsgIn_sets_bit_one_and_the_TermChar_byte_when_enabled()
    {
        var transfer = UsbTmcCodec.WriteRequestDevDepMsgIn(
            new UsbTmcRequestDevDepMsgIn(
                BTag: 2,
                TransferSize: 1024,
                TermCharEnabled: true,
                TermChar: (byte)'\n'
            )
        );

        // bmTransferAttributes bit 1 is TermChar-enabled; bit 0 is not
        // used by this header.
        transfer[8].ShouldBe((byte)0x02);
        transfer[9].ShouldBe((byte)0x0A);
    }

    [Fact]
    public void ReadRequestDevDepMsgIn_reads_the_golden_back_into_its_fields()
    {
        var request = UsbTmcCodec.ReadRequestDevDepMsgIn(UsbTmcGoldenTransfers.RequestDevDepMsgIn);

        request.BTag.ShouldBe((byte)2);
        request.TransferSize.ShouldBe(1024u);
        request.TermCharEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ReadRequestDevDepMsgIn_rejects_a_broken_bTagInverse()
    {
        var transfer = UsbTmcGoldenTransfers.RequestDevDepMsgIn;
        transfer[2] = 0x02;

        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadRequestDevDepMsgIn(transfer));
    }

    [Fact]
    public void WriteDevDepMsgIn_emits_the_twenty_byte_answer_golden()
    {
        var transfer = UsbTmcCodec.WriteDevDepMsgIn(
            new UsbTmcDevDepMsgIn(
                BTag: 2,
                EndOfMessage: true,
                Payload: UsbTmcGoldenTransfers.MockAnswer
            )
        );

        transfer.ShouldBe(UsbTmcGoldenTransfers.DevDepMsgInMock);
    }

    [Fact]
    public void ReadDevDepMsgIn_reads_the_answer_golden_back_into_its_fields()
    {
        var message = UsbTmcCodec.ReadDevDepMsgIn(UsbTmcGoldenTransfers.DevDepMsgInMock);

        message.BTag.ShouldBe((byte)2);
        message.EndOfMessage.ShouldBeTrue();
        message.TransferSize.ShouldBe(5u);
        message.Payload.ShouldBe(UsbTmcGoldenTransfers.MockAnswer);
    }

    [Fact]
    public void ReadMsgId_names_the_header_without_decoding_the_rest_of_it()
    {
        UsbTmcCodec
            .ReadMsgId(UsbTmcGoldenTransfers.DevDepMsgOutIdn)
            .ShouldBe(UsbTmcConstants.MsgIdDevDepMsgOut);
        UsbTmcCodec
            .ReadMsgId(UsbTmcGoldenTransfers.RequestDevDepMsgIn)
            .ShouldBe(UsbTmcConstants.MsgIdRequestDevDepMsgIn);
    }

    [Fact]
    public void ReadMsgId_rejects_a_transfer_too_short_to_hold_a_header()
    {
        Should.Throw<InvalidDataException>(() => UsbTmcCodec.ReadMsgId([]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(6, 8)]
    public void AlignedPayloadLength_rounds_up_to_the_four_byte_boundary(
        int payloadLength,
        int expected
    )
    {
        UsbTmcConstants.AlignedPayloadLength(payloadLength).ShouldBe(expected);
    }
}
