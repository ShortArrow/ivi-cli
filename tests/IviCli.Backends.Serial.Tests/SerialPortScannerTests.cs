using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Application.Devices;
using IviCli.Backends.Serial;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Serial.Tests;

/// <summary>
/// <c>visa scan</c> lists the serial ports and, for one no board names, the
/// <c>visa asrl add</c> line that registers it (ADR 0055 §3).
/// </summary>
public sealed class SerialPortScannerTests
{
    [Fact]
    public async Task Each_unregistered_port_gets_its_own_free_board()
    {
        var config = Boards((1, "/dev/ttyS0"));
        var scanner = Scanner(config, ["/dev/ttyUSB0", "/dev/ttyS0", "/dev/ttyACM0"]);

        var found = await ScanAsync(scanner);

        found.ShouldBe([
            ("ASRL/dev/ttyACM0::INSTR", "register with: ivicli visa asrl add 2 /dev/ttyACM0"),
            ("ASRL/dev/ttyS0::INSTR", "board 1"),
            ("ASRL/dev/ttyUSB0::INSTR", "register with: ivicli visa asrl add 3 /dev/ttyUSB0"),
        ]);
    }

    [Fact]
    public async Task A_board_registered_under_another_name_of_the_port_names_it()
    {
        var config = Boards((2, "/dev/serial/by-id/usb-FTDI_A1"));
        var ports = new LinkedPorts().Link("/dev/serial/by-id/usb-FTDI_A1", "/dev/ttyUSB0");
        var scanner = Scanner(config, ["/dev/ttyUSB0"], ports);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRL/dev/ttyUSB0::INSTR", "board 2")]);
    }

    [Fact]
    public async Task A_port_listed_under_another_name_is_matched_to_its_board()
    {
        var config = Boards((3, "COM3"));
        var ports = new LinkedPorts().Link("com3", "COM3");
        var scanner = Scanner(config, ["com3"], ports);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLcom3::INSTR", "board 3")]);
    }

    [Fact]
    public async Task A_board_whose_port_is_absent_keeps_its_number()
    {
        var config = Boards((1, "/dev/ttyUSB9"));
        var scanner = Scanner(config, ["COM3"]);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLCOM3::INSTR", "register with: ivicli visa asrl add 2 COM3")]);
    }

    [Fact]
    public async Task A_board_a_device_already_uses_is_not_suggested()
    {
        var doc = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    DeviceName.From("dmm").ShouldBeOk(),
                    VisaResource.Parse("ASRL1::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(2000).ShouldBeOk()
                )
            )
            .ShouldBeOk();
        var scanner = Scanner(new FakeConfigStore(doc), ["COM3"]);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLCOM3::INSTR", "register with: ivicli visa asrl add 2 COM3")]);
    }

    [Fact]
    public async Task A_port_name_a_board_cannot_be_registered_under_is_left_out()
    {
        var scanner = Scanner(new FakeConfigStore(ConfigDocument.Empty), [" COM4", "COM3"]);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLCOM3::INSTR", "register with: ivicli visa asrl add 1 COM3")]);
    }

    [Fact]
    public async Task A_port_named_only_by_digits_is_left_out()
    {
        var scanner = Scanner(new FakeConfigStore(ConfigDocument.Empty), ["7", "COM3"]);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLCOM3::INSTR", "register with: ivicli visa asrl add 1 COM3")]);
    }

    [Fact]
    public async Task On_Windows_a_port_SerialPort_cannot_open_is_left_out()
    {
        var scanner = Scanner(
            new FakeConfigStore(ConfigDocument.Empty),
            ["CNCA0", "COM#", "COM3"],
            onWindows: true
        );

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLCOM3::INSTR", "register with: ivicli visa asrl add 1 COM3")]);
    }

    [Fact]
    public async Task Ports_are_listed_without_a_board_when_the_configuration_cannot_be_read()
    {
        var config = new FakeConfigStore(ConfigDocument.Empty);
        config.FailNextLoadWith("disk gone");
        var scanner = Scanner(config, ["COM3"]);

        var found = await ScanAsync(scanner);

        found.ShouldBe([("ASRLCOM3::INSTR", (string?)null)]);
    }

    private static FakeConfigStore Boards(params (int Board, string Port)[] boards)
    {
        var doc = ConfigDocument.Empty;
        foreach (var (board, port) in boards)
        {
            doc = doc.AddAsrlBoard(AsrlBoardRegistration.From(board, port).ShouldBeOk())
                .ShouldBeOk();
        }
        return new FakeConfigStore(doc);
    }

    private static SerialPortScanner Scanner(
        FakeConfigStore config,
        string[] present,
        LinkedPorts? ports = null,
        bool onWindows = false
    ) => new(config, ports ?? new LinkedPorts(), () => present, onWindows);

    private static async Task<(string Resource, string? Detail)[]> ScanAsync(
        SerialPortScanner scanner
    )
    {
        var result = await scanner.ScanAsync(
            ScanOptions.Default,
            TestContext.Current.CancellationToken
        );
        return result.ShouldBeOk().Select(r => (r.Resource.ToCanonical(), r.Detail)).ToArray();
    }

    /// <summary>Ports where <see cref="Link"/> names another name for a port.</summary>
    private sealed class LinkedPorts : ISerialPorts
    {
        private readonly Dictionary<string, string> _links = [];

        public LinkedPorts Link(string name, string target)
        {
            _links[name] = target;
            return this;
        }

        public string Resolve(string port) => _links.GetValueOrDefault(port, port);

        public bool Exists(string port) => true;
    }
}
