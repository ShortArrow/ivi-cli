using System.IO.Ports;
using System.Text;
using IviCli.Backends.Serial;
using IviCli.Domain.Devices;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Serial.Tests;

/// <summary>
/// How a device's serial settings land on <see cref="SerialPort"/>, read back
/// from the port object before it is opened: a pseudo-terminal has no baud
/// rate or parity to check them against (ADR 0055 §4).
/// </summary>
public sealed class SerialPortLineFactoryTests
{
    [Fact]
    public void Every_setting_lands_on_the_port()
    {
        using var port = new SerialPort();
        var settings = SerialSettings
            .From(115200, 7, SerialParity.Mark, SerialStopBits.Two, SerialFlowControl.RtsCts)
            .ShouldBeOk();

        SerialPortLineFactory.Configure(port, "COM7", settings, TimeSpan.FromMilliseconds(1500));

        port.PortName.ShouldBe("COM7");
        port.BaudRate.ShouldBe(115200);
        port.DataBits.ShouldBe(7);
        port.Parity.ShouldBe(Parity.Mark);
        port.StopBits.ShouldBe(StopBits.Two);
        port.Handshake.ShouldBe(Handshake.RequestToSend);
        port.ReadTimeout.ShouldBe(1500);
        port.WriteTimeout.ShouldBe(1500);
    }

    [Theory]
    [InlineData(SerialParity.None, Parity.None)]
    [InlineData(SerialParity.Odd, Parity.Odd)]
    [InlineData(SerialParity.Even, Parity.Even)]
    [InlineData(SerialParity.Mark, Parity.Mark)]
    [InlineData(SerialParity.Space, Parity.Space)]
    public void Each_parity_maps(SerialParity parity, Parity expected)
    {
        using var port = new SerialPort();

        SerialPortLineFactory.Configure(
            port,
            "COM7",
            With(parity: parity),
            TimeSpan.FromSeconds(1)
        );

        port.Parity.ShouldBe(expected);
    }

    [Theory]
    [InlineData(SerialStopBits.One, StopBits.One)]
    [InlineData(SerialStopBits.OnePointFive, StopBits.OnePointFive)]
    [InlineData(SerialStopBits.Two, StopBits.Two)]
    public void Each_stop_bits_value_maps(SerialStopBits stopBits, StopBits expected)
    {
        using var port = new SerialPort();

        SerialPortLineFactory.Configure(
            port,
            "COM7",
            With(stopBits: stopBits),
            TimeSpan.FromSeconds(1)
        );

        port.StopBits.ShouldBe(expected);
    }

    [Theory]
    [InlineData(SerialFlowControl.None, Handshake.None)]
    [InlineData(SerialFlowControl.XonXoff, Handshake.XOnXOff)]
    [InlineData(SerialFlowControl.RtsCts, Handshake.RequestToSend)]
    public void Each_flow_control_maps(SerialFlowControl flowControl, Handshake expected)
    {
        using var port = new SerialPort();

        SerialPortLineFactory.Configure(
            port,
            "COM7",
            With(flowControl: flowControl),
            TimeSpan.FromSeconds(1)
        );

        port.Handshake.ShouldBe(expected);
    }

    /// <summary>Text is Latin-1, so every byte reaches the reply unchanged.</summary>
    [Fact]
    public void Text_is_Latin1()
    {
        using var port = new SerialPort();

        SerialPortLineFactory.Configure(
            port,
            "COM7",
            SerialSettings.Default,
            TimeSpan.FromSeconds(1)
        );

        port.Encoding.ShouldBe(Encoding.Latin1);
    }

    /// <summary>
    /// <c>SerialPort</c> adds the <c>\\.\</c> prefix itself and refuses a
    /// name that already has it, so the prefix a user may write is removed.
    /// </summary>
    [Fact]
    public void A_Windows_device_prefix_is_removed()
    {
        using var port = new SerialPort();

        SerialPortLineFactory.Configure(
            port,
            @"\\.\COM10",
            SerialSettings.Default,
            TimeSpan.FromSeconds(1)
        );

        port.PortName.ShouldBe("COM10");
    }

    /// <summary>
    /// A device timeout of 0 means not to wait; <c>SerialPort</c> refuses 0,
    /// so the port gets its shortest timeout, 1 ms.
    /// </summary>
    [Fact]
    public void A_zero_timeout_becomes_the_shortest_the_port_takes()
    {
        using var port = new SerialPort();

        SerialPortLineFactory.Configure(port, "COM7", SerialSettings.Default, TimeSpan.Zero);

        port.ReadTimeout.ShouldBe(1);
        port.WriteTimeout.ShouldBe(1);
    }

    /// <summary>
    /// On Linux and macOS, <c>SerialPort</c> reports every failed open as
    /// access denied and keeps the reason, an errno, in the inner exception.
    /// </summary>
    [Theory]
    [InlineData(16, "in-use")]
    [InlineData(2, "the port does not exist")]
    [InlineData(13, "permission denied")]
    public void A_failed_open_on_Unix_is_told_apart_by_its_errno(int errno, string expected)
    {
        var failure = new UnauthorizedAccessException(
            "Access to the port '/dev/ttyUSB0' is denied.",
            new IOException("errno", errno)
        );

        var translated = SerialPortLineFactory.Translate(failure, windows: false);

        if (expected == "in-use")
        {
            translated.ShouldBeOfType<UnauthorizedAccessException>();
        }
        else
        {
            translated
                .ShouldBeOfType<SerialLineUnavailableException>()
                .Message.ShouldStartWith(expected);
        }
    }

    /// <summary>
    /// On Windows a missing port fails the open with a file-not-found error
    /// whose message names the port; it says so in words that do not.
    /// </summary>
    [Fact]
    public void A_missing_port_on_Windows_says_it_does_not_exist()
    {
        var failure = new FileNotFoundException("The port 'COM250' does not exist.");

        SerialPortLineFactory
            .Translate(failure, windows: true)
            .ShouldBeOfType<SerialLineUnavailableException>()
            .Message.ShouldBe("the port does not exist");
    }

    [Fact]
    public void A_failed_open_on_Windows_is_left_as_it_is()
    {
        var failure = new UnauthorizedAccessException("denied", new IOException("errno", 2));

        SerialPortLineFactory.Translate(failure, windows: true).ShouldBeSameAs(failure);
    }

    private static SerialSettings With(
        SerialParity parity = SerialParity.None,
        SerialStopBits stopBits = SerialStopBits.One,
        SerialFlowControl flowControl = SerialFlowControl.None
    ) => SerialSettings.From(9600, 8, parity, stopBits, flowControl).ShouldBeOk();
}
