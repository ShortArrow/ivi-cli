using IviCli.Domain.Protocols;
using Shouldly;

namespace IviCli.Server.Tests;

public sealed class Vxi11InterruptCodecTests
{
    [Fact]
    public void RemoteFunc_round_trip_preserves_every_field()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        var input = new DeviceRemoteFunc(
            HostAddr: 0x7F000001,
            HostPort: 50000,
            ProgNum: Vxi11Constants.InterruptProgram,
            ProgVers: Vxi11Constants.InterruptVersion,
            ProgFamily: Vxi11Constants.ProgFamilyTcp
        );
        Vxi11InterruptCodec.WriteRemoteFunc(writer, input);

        var reader = new Vxi11XdrCodec.XdrReader(writer.ToArray());
        var output = Vxi11InterruptCodec.ReadRemoteFunc(ref reader);

        output.ShouldBe(input);
    }

    [Fact]
    public void EnableSrqParms_round_trip_handles_8_byte_handle()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        var input = new DeviceEnableSrqParms(
            Lid: 42,
            Enable: true,
            Handle: [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]
        );
        Vxi11InterruptCodec.WriteEnableSrqParms(writer, input);

        var reader = new Vxi11XdrCodec.XdrReader(writer.ToArray());
        var output = Vxi11InterruptCodec.ReadEnableSrqParms(ref reader);

        output.Lid.ShouldBe(42);
        output.Enable.ShouldBeTrue();
        output.Handle.ShouldBe(input.Handle);
    }

    [Fact]
    public void EnableSrqParms_disable_encodes_zero_bool()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        Vxi11InterruptCodec.WriteEnableSrqParms(
            writer,
            new DeviceEnableSrqParms(1, Enable: false, [])
        );

        var reader = new Vxi11XdrCodec.XdrReader(writer.ToArray());
        var output = Vxi11InterruptCodec.ReadEnableSrqParms(ref reader);

        output.Enable.ShouldBeFalse();
        output.Handle.Length.ShouldBe(0);
    }

    [Fact]
    public void SrqParms_round_trip_preserves_handle()
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        var handle = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        Vxi11InterruptCodec.WriteSrqParms(writer, new DeviceSrqParms(handle));

        var reader = new Vxi11XdrCodec.XdrReader(writer.ToArray());
        var output = Vxi11InterruptCodec.ReadSrqParms(ref reader);

        output.Handle.ShouldBe(handle);
    }
}
