using IviCli.Application.Devices;
using IviCli.Domain.Devices;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

/// <summary>
/// <c>visa add</c> takes serial settings for an ASRL device and terminators
/// for any device (ADR 0055 §4, §5).
/// </summary>
public sealed class AddDeviceSerialOptionsTests
{
    private static async Task<Device> Saved(FakeConfigStore store) =>
        (await store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBeOk().Devices[0];

    [Fact]
    public async Task Serial_options_on_an_ASRL_device_override_the_defaults()
    {
        // Given
        var store = new FakeConfigStore();
        var handler = new AddDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new AddDeviceCommand("dmm", "ASRL1::INSTR", 3000)
            {
                Serial = new SerialOptions(Baud: 19200, Parity: "even", StopBits: "1.5"),
            },
            TestContext.Current.CancellationToken
        );

        // Then
        var expected = SerialSettings
            .From(19200, 8, SerialParity.Even, SerialStopBits.OnePointFive, SerialFlowControl.None)
            .ShouldBeOk();
        result.ShouldBeOk().Serial.ShouldBe(expected);
        (await Saved(store)).Serial.ShouldBe(expected);
    }

    [Fact]
    public async Task Serial_options_on_a_network_device_fail()
    {
        // Given
        var store = new FakeConfigStore();
        var handler = new AddDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new AddDeviceCommand("psu", "TCPIP0::host::5025::SOCKET", 3000)
            {
                Serial = new SerialOptions(Baud: 19200),
            },
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<AddDeviceSerialOnNonSerial>();
        (await store.LoadAsync(TestContext.Current.CancellationToken))
            .ShouldBeOk()
            .Devices.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("parity", "sometimes")]
    [InlineData("stop_bits", "3")]
    [InlineData("flow_control", "dtr_dsr")]
    [InlineData("data_bits", "9")]
    [InlineData("baud", "0")]
    public async Task An_invalid_serial_option_fails_and_names_it(string field, string value)
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());
        var serial = field switch
        {
            "parity" => new SerialOptions(Parity: value),
            "stop_bits" => new SerialOptions(StopBits: value),
            "flow_control" => new SerialOptions(FlowControl: value),
            "data_bits" => new SerialOptions(
                DataBits: int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            ),
            _ => new SerialOptions(
                Baud: int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            ),
        };

        // When
        var result = await handler.HandleAsync(
            new AddDeviceCommand("dmm", "ASRL1::INSTR", 3000) { Serial = serial },
            TestContext.Current.CancellationToken
        );

        // Then
        result
            .ShouldBeError()
            .ShouldBeOfType<AddDeviceInvalidSerialSetting>()
            .Field.ShouldBe(field);
    }

    [Theory]
    [InlineData("lf", "\n")]
    [InlineData("CR", "\r")]
    [InlineData("crlf", "\r\n")]
    [InlineData(@"\n", "\n")]
    [InlineData(@"\r\n", "\r\n")]
    public async Task A_terminator_is_named_or_written_with_escapes(string option, string chars)
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());

        // When
        var result = await handler.HandleAsync(
            new AddDeviceCommand("psu", "TCPIP0::host::5025::SOCKET", 3000)
            {
                ReadTermination = option,
                WriteTermination = option,
            },
            TestContext.Current.CancellationToken
        );

        // Then
        var device = result.ShouldBeOk();
        device.ReadTermination!.Value.ShouldBe(chars);
        device.WriteTermination!.Value.ShouldBe(chars);
    }

    [Theory]
    [InlineData(";")]
    [InlineData("lfcr")]
    [InlineData(@"\n\r")]
    public async Task An_invalid_terminator_fails(string option)
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());

        // When
        var result = await handler.HandleAsync(
            new AddDeviceCommand("psu", "TCPIP0::host::5025::SOCKET", 3000)
            {
                WriteTermination = option,
            },
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<AddDeviceInvalidTermination>().Raw.ShouldBe(option);
    }

    /// <summary>
    /// A terminator on a transport that ends messages itself is saved, and
    /// the added device says it has no effect so the caller can warn.
    /// </summary>
    [Fact]
    public async Task A_terminator_on_a_HiSLIP_device_is_saved_and_reported_as_ignored()
    {
        // Given
        var store = new FakeConfigStore();
        var handler = new AddDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new AddDeviceCommand("scope", "TCPIP0::host::hislip0::INSTR", 3000)
            {
                ReadTermination = "cr",
            },
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeOk().IgnoresTermination.ShouldBeTrue();
        (await Saved(store)).ReadTermination.ShouldBe(Termination.Cr);
    }
}
