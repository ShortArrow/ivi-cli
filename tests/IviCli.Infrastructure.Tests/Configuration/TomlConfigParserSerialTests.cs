using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.Infrastructure.Configuration;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Configuration;

/// <summary>
/// The <c>[[asrl]]</c> table, <c>[devices.serial]</c> and the termination
/// fields in <c>config.toml</c> (ADR 0055 §3-§5).
/// </summary>
public class TomlConfigParserSerialTests
{
    private static Device Dev(string resource) =>
        new(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static SerialSettings Settings(
        int baud = 9600,
        int dataBits = 8,
        SerialParity parity = SerialParity.None,
        SerialStopBits stopBits = SerialStopBits.One,
        SerialFlowControl flowControl = SerialFlowControl.None
    ) => SerialSettings.From(baud, dataBits, parity, stopBits, flowControl).ShouldBeOk();

    private static ConfigDocument RoundTrip(ConfigDocument config) =>
        TomlConfigParser.Parse(TomlConfigParser.Serialize(config)).ShouldBeOk();

    [Fact]
    public void Parse_AsrlTable_RegistersTheBoards()
    {
        // Given
        var toml = """
            [[asrl]]
            board = 1
            port = "/dev/ttyUSB0"

            [[asrl]]
            board = 2
            port = "COM3"
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result
            .ShouldBeOk()
            .AsrlBoards.ShouldBe([
                AsrlBoardRegistration.From(1, "/dev/ttyUSB0").ShouldBeOk(),
                AsrlBoardRegistration.From(2, "COM3").ShouldBeOk(),
            ]);
    }

    [Fact]
    public void Parse_AsrlPortRegisteredTwice_Fails()
    {
        // Given
        var toml = """
            [[asrl]]
            board = 1
            port = "/dev/ttyUSB0"

            [[asrl]]
            board = 2
            port = "/dev/ttyUSB0"
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError();
    }

    [Fact]
    public void Parse_SerialTable_SetsEverySetting()
    {
        // Given
        var toml = """
            [[devices]]
            name = "dmm"
            resource = "ASRL1::INSTR"
            timeout_ms = 3000

            [devices.serial]
            baud = 19200
            data_bits = 7
            parity = "even"
            stop_bits = 2
            flow_control = "rts_cts"
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result
            .ShouldBeOk()
            .Devices[0]
            .Serial.ShouldBe(
                Settings(19200, 7, SerialParity.Even, SerialStopBits.Two, SerialFlowControl.RtsCts)
            );
    }

    [Fact]
    public void Parse_SerialTableWithSomeSettings_DefaultsTheRest()
    {
        // Given
        var toml = """
            [[devices]]
            name = "dmm"
            resource = "ASRL1::INSTR"
            timeout_ms = 3000

            [devices.serial]
            baud = 115200
            stop_bits = 1.5
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result
            .ShouldBeOk()
            .Devices[0]
            .Serial.ShouldBe(Settings(115200, stopBits: SerialStopBits.OnePointFive));
    }

    [Theory]
    [InlineData("parity = \"sometimes\"")]
    [InlineData("stop_bits = 3")]
    [InlineData("data_bits = 9")]
    [InlineData("baud = 0")]
    [InlineData("flow_control = \"dtr_dsr\"")]
    [InlineData("baud = \"fast\"")]
    public void Parse_InvalidSerialSetting_Fails(string line)
    {
        // Given
        var toml = $"""
            [[devices]]
            name = "dmm"
            resource = "ASRL1::INSTR"
            timeout_ms = 3000

            [devices.serial]
            {line}
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError();
    }

    [Fact]
    public void Parse_SerialTableOnANetworkDevice_Fails()
    {
        // Given
        var toml = """
            [[devices]]
            name = "psu"
            resource = "TCPIP0::host::5025::SOCKET"
            timeout_ms = 3000

            [devices.serial]
            baud = 19200
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError();
    }

    [Fact]
    public void Parse_Terminations_SetsBoth()
    {
        // Given
        var toml = """
            [[devices]]
            name = "psu"
            resource = "TCPIP0::host::5025::SOCKET"
            timeout_ms = 3000
            read_termination = "\r\n"
            write_termination = "\r"
            """;

        // When
        var device = TomlConfigParser.Parse(toml).ShouldBeOk().Devices[0];

        // Then
        device.ReadTermination.ShouldBe(Termination.CrLf);
        device.WriteTermination.ShouldBe(Termination.Cr);
    }

    [Fact]
    public void Parse_InvalidTermination_Fails()
    {
        // Given
        var toml = """
            [[devices]]
            name = "psu"
            resource = "TCPIP0::host::5025::SOCKET"
            timeout_ms = 3000
            read_termination = ";"
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError();
    }

    [Fact]
    public void Serialize_SerialDeviceTerminationsAndBoards_RoundTrip()
    {
        // Given
        var config = ConfigDocument
            .Empty.AddDevice(
                Dev("ASRL1::INSTR") with
                {
                    Serial = Settings(
                        19200,
                        7,
                        SerialParity.Mark,
                        SerialStopBits.OnePointFive,
                        SerialFlowControl.XonXoff
                    ),
                    ReadTermination = Termination.CrLf,
                    WriteTermination = Termination.Cr,
                }
            )
            .ShouldBeOk()
            .AddAsrlBoard(AsrlBoardRegistration.From(1, "/dev/ttyUSB0").ShouldBeOk())
            .ShouldBeOk();

        // When
        var parsed = RoundTrip(config);

        // Then
        parsed.ShouldBe(config);
    }

    [Theory]
    [InlineData("ASRL\\\\.\\COM10::INSTR")]
    [InlineData("ASRL/dev/serial/by-id/usb-FTDI \"quoted\"::INSTR")]
    public void Serialize_PortNameNeedingEscapes_RoundTrips(string resource)
    {
        // Given
        var config = ConfigDocument.Empty.AddDevice(Dev(resource)).ShouldBeOk();

        // When
        var parsed = RoundTrip(config);

        // Then
        parsed.Devices[0].Resource.ToCanonical().ShouldBe(resource);
    }

    [Fact]
    public void Serialize_WritesOnlySettingsThatDifferFromTheDefaults()
    {
        // Given
        var config = ConfigDocument
            .Empty.AddDevice(Dev("ASRL1::INSTR") with { Serial = Settings(baud: 19200) })
            .ShouldBeOk();

        // When
        var toml = TomlConfigParser.Serialize(config);

        // Then
        toml.ShouldContain("[devices.serial]");
        toml.ShouldContain("baud = 19200");
        toml.ShouldNotContain("data_bits");
        toml.ShouldNotContain("parity");
        toml.ShouldNotContain("stop_bits");
        toml.ShouldNotContain("flow_control");
    }

    [Fact]
    public void Serialize_DefaultSerialSettings_WritesNoSerialTable()
    {
        // Given
        var config = ConfigDocument
            .Empty.AddDevice(Dev("ASRL1::INSTR") with { Serial = SerialSettings.Default })
            .ShouldBeOk();

        // When
        var toml = TomlConfigParser.Serialize(config);

        // Then
        toml.ShouldNotContain("[devices.serial]");
    }
}
