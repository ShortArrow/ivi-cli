using IviCli.Domain.Protocols;
using Shouldly;
using Xunit;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Characteristic tests for the pure HiSLIP framer. Verifies the 16-byte
/// header layout per IVI-6.1 §10: 'S' prologue, type, control code, 4-byte
/// big-endian message parameter, 8-byte big-endian payload length, 1 byte
/// reserved.
/// </summary>
public sealed class HiSlipMessageTests
{
    [Fact]
    public void WriteHeader_then_ReadHeader_round_trips()
    {
        var buffer = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            buffer,
            HiSlipMessageType.DataEnd,
            controlCode: 0,
            messageParameter: 0xDEADBEEF,
            payloadLength: 0x0123_4567_89AB_CDEF
        );

        var header = HiSlipMessage.ReadHeader(buffer);
        header.Type.ShouldBe(HiSlipMessageType.DataEnd);
        header.ControlCode.ShouldBe<byte>(0);
        header.MessageParameter.ShouldBe(0xDEADBEEFu);
        header.PayloadLength.ShouldBe(0x0123_4567_89AB_CDEFu);
    }

    [Fact]
    public void WriteHeader_places_prologue_at_offset_zero()
    {
        var buffer = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            buffer,
            HiSlipMessageType.Initialize,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: 0
        );
        buffer[0].ShouldBe(HiSlipMessage.Prologue);
    }

    [Fact]
    public void ReadHeader_rejects_wrong_prologue()
    {
        var buffer = new byte[HiSlipMessage.HeaderSize];
        buffer[0] = 0x42;
        Should.Throw<InvalidDataException>(() => HiSlipMessage.ReadHeader(buffer));
    }

    [Fact]
    public void WriteHeader_rejects_too_small_destination()
    {
        var buffer = new byte[HiSlipMessage.HeaderSize - 1];
        Should.Throw<ArgumentException>(() =>
            HiSlipMessage.WriteHeader(buffer, HiSlipMessageType.Data, 0, 0, 0)
        );
    }

    [Fact]
    public void Header_size_is_sixteen_bytes()
    {
        HiSlipMessage.HeaderSize.ShouldBe(16);
    }

    [Theory]
    [InlineData(HiSlipMessageType.Initialize)]
    [InlineData(HiSlipMessageType.InitializeResponse)]
    [InlineData(HiSlipMessageType.FatalError)]
    [InlineData(HiSlipMessageType.Data)]
    [InlineData(HiSlipMessageType.DataEnd)]
    [InlineData(HiSlipMessageType.AsyncInitialize)]
    [InlineData(HiSlipMessageType.AsyncInitializeResponse)]
    [InlineData(HiSlipMessageType.AsyncMaximumMessageSize)]
    [InlineData(HiSlipMessageType.AsyncMaximumMessageSizeResponse)]
    [InlineData(HiSlipMessageType.AsyncDeviceClear)]
    [InlineData(HiSlipMessageType.AsyncDeviceClearAcknowledge)]
    [InlineData(HiSlipMessageType.AsyncLock)]
    [InlineData(HiSlipMessageType.AsyncLockResponse)]
    [InlineData(HiSlipMessageType.ServiceRequest)]
    public void WriteHeader_round_trips_each_type(HiSlipMessageType type)
    {
        var buffer = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(buffer, type, 0, 0, 0);
        HiSlipMessage.ReadHeader(buffer).Type.ShouldBe(type);
    }

    [Fact]
    public void HiSlip_message_types_follow_IVI_6_1_spec_values()
    {
        // IVI-6.1 §10 table — these literals are the contract with real
        // VISA clients (NI / Keysight / R&S / PyVISA). If a value drifts
        // out of this table the interop with real instruments breaks
        // silently at the wire level. Update this test in lockstep with
        // any deliberate spec revision.
        ((byte)HiSlipMessageType.Initialize).ShouldBe<byte>(0);
        ((byte)HiSlipMessageType.InitializeResponse).ShouldBe<byte>(1);
        ((byte)HiSlipMessageType.FatalError).ShouldBe<byte>(2);
        ((byte)HiSlipMessageType.Error).ShouldBe<byte>(3);
        ((byte)HiSlipMessageType.AsyncLock).ShouldBe<byte>(4);
        ((byte)HiSlipMessageType.AsyncLockResponse).ShouldBe<byte>(5);
        ((byte)HiSlipMessageType.Data).ShouldBe<byte>(6);
        ((byte)HiSlipMessageType.DataEnd).ShouldBe<byte>(7);
        ((byte)HiSlipMessageType.DeviceClearComplete).ShouldBe<byte>(8);
        ((byte)HiSlipMessageType.DeviceClearAcknowledge).ShouldBe<byte>(9);
        ((byte)HiSlipMessageType.AsyncMaximumMessageSize).ShouldBe<byte>(15);
        ((byte)HiSlipMessageType.AsyncMaximumMessageSizeResponse).ShouldBe<byte>(16);
        ((byte)HiSlipMessageType.AsyncInitialize).ShouldBe<byte>(17);
        ((byte)HiSlipMessageType.AsyncInitializeResponse).ShouldBe<byte>(18);
        ((byte)HiSlipMessageType.AsyncDeviceClear).ShouldBe<byte>(19);
        ((byte)HiSlipMessageType.ServiceRequest).ShouldBe<byte>(20);
        ((byte)HiSlipMessageType.AsyncDeviceClearAcknowledge).ShouldBe<byte>(23);
    }
}
