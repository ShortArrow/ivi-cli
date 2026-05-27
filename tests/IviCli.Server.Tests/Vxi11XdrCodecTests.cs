using IviCli.Domain.Protocols;
using IviCli.Server.Vxi11;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// Pure round-trip + boundary tests for the hand-rolled XDR codec.
/// Each test pins one wire-format property (big-endian integers,
/// 4-byte string padding, last-fragment record marking) so a regression
/// in the codec surfaces here, not on a smoke test that runs the
/// gateway end-to-end.
/// </summary>
public sealed class Vxi11XdrCodecTests
{
    [Fact]
    public void UInt32_round_trip_is_big_endian()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(0xDEADBEEFu);
        var bytes = writer.ToArray();
        bytes.ShouldBe([0xDE, 0xAD, 0xBE, 0xEF]);

        var reader = new Vxi11XdrCodec.XdrReader(bytes);
        reader.ReadUInt32().ShouldBe(0xDEADBEEFu);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void Int32_round_trip_handles_negative_two_complement()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteInt32(-1);
        var bytes = writer.ToArray();
        bytes.ShouldBe([0xFF, 0xFF, 0xFF, 0xFF]);

        var reader = new Vxi11XdrCodec.XdrReader(bytes);
        reader.ReadInt32().ShouldBe(-1);
    }

    [Fact]
    public void Opaque_pads_to_four_byte_boundary()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteOpaque([0xAA, 0xBB, 0xCC]);
        var bytes = writer.ToArray();
        // 4-byte length (0x00000003) + 3 body bytes + 1 zero pad byte
        bytes.ShouldBe([0x00, 0x00, 0x00, 0x03, 0xAA, 0xBB, 0xCC, 0x00]);

        var reader = new Vxi11XdrCodec.XdrReader(bytes);
        reader.ReadOpaque().ShouldBe([0xAA, 0xBB, 0xCC]);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void Opaque_with_length_divisible_by_four_writes_no_padding()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteOpaque([0x11, 0x22, 0x33, 0x44]);
        var bytes = writer.ToArray();
        bytes.Length.ShouldBe(8);
        bytes[7].ShouldBe((byte)0x44);
    }

    [Fact]
    public void String_round_trip_is_ascii_and_padded()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteString("inst0");
        var bytes = writer.ToArray();
        // 5 chars + 3 pad bytes = 8 body bytes + 4 length bytes = 12
        bytes.Length.ShouldBe(12);
        var reader = new Vxi11XdrCodec.XdrReader(bytes);
        reader.ReadString().ShouldBe("inst0");
    }

    [Fact]
    public async Task RecordMarking_round_trip_preserves_payload()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        using var memory = new MemoryStream();
        await Vxi11RecordFraming.WriteRecordAsync(memory, payload, default);

        var framed = memory.ToArray();
        framed.Length.ShouldBe(4 + payload.Length);
        // LAST_FRAGMENT bit set, length 5
        framed[0].ShouldBe((byte)0x80);
        framed[1].ShouldBe((byte)0);
        framed[2].ShouldBe((byte)0);
        framed[3].ShouldBe((byte)5);

        memory.Position = 0;
        var roundTripped = await Vxi11RecordFraming.ReadRecordAsync(memory, default);
        roundTripped.ShouldBe(payload);
    }

    [Fact]
    public async Task RecordMarking_rejects_non_terminal_fragment()
    {
        // Manually craft a fragment header WITHOUT the LAST_FRAGMENT bit.
        var header = new byte[] { 0x00, 0x00, 0x00, 0x04 };
        var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        using var memory = new MemoryStream();
        memory.Write(header);
        memory.Write(body);
        memory.Position = 0;

        await Should.ThrowAsync<NotSupportedException>(() =>
            Vxi11RecordFraming.ReadRecordAsync(memory, default)
        );
    }
}
