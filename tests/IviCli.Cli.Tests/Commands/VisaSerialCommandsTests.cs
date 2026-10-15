using System.CommandLine;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Cli.Commands;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// <c>visa add</c> with serial options and terminators, and <c>visa asrl
/// add</c>, run end to end against an in-memory configuration (ADR 0055
/// §3-§5).
/// </summary>
[Collection(ConsoleRedirection.Name)]
public sealed class VisaSerialCommandsTests
{
    [Fact]
    public async Task Adding_a_network_device_without_serial_options_saves_no_serial_settings()
    {
        var store = new FakeConfigStore();

        var (exitCode, _, _) = await Run(
            VisaAddCommand.Build(Services(store)),
            ["psu", "TCPIP0::host::5025::SOCKET"]
        );

        exitCode.ShouldBe(0);
        (await Saved(store)).Devices.ShouldHaveSingleItem().Serial.ShouldBeNull();
    }

    [Fact]
    public async Task A_terminator_on_a_HiSLIP_device_is_saved_with_a_warning()
    {
        var store = new FakeConfigStore();

        var (exitCode, _, error) = await Run(
            VisaAddCommand.Build(Services(store)),
            ["scope", "TCPIP0::host::hislip0::INSTR", "--read-termination", "cr"]
        );

        exitCode.ShouldBe(0);
        error.ShouldContain("warning: scope sets a terminator");
        (await Saved(store)).Devices.ShouldHaveSingleItem().ReadTermination.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_terminator_on_a_SOCKET_device_is_saved_without_a_warning()
    {
        var store = new FakeConfigStore();

        var (exitCode, _, error) = await Run(
            VisaAddCommand.Build(Services(store)),
            ["psu", "TCPIP0::host::5025::SOCKET", "--read-termination", "cr"]
        );

        exitCode.ShouldBe(0);
        error.ShouldNotContain("warning");
    }

    [Fact]
    public async Task Registering_a_board_that_is_taken_exits_with_the_device_error_code()
    {
        var store = new FakeConfigStore(
            ConfigDocument
                .Empty.AddAsrlBoard(AsrlBoardRegistration.From(1, "COM3").ShouldBeOk())
                .ShouldBeOk()
        );

        var (exitCode, _, error) = await Run(
            VisaAsrlCommand.Build(Services(store)),
            ["add", "1", "COM4"]
        );

        exitCode.ShouldBe(ExitCodeMapper.DeviceError);
        error.ShouldContain("already registered");
    }

    [Fact]
    public async Task Registering_a_board_for_an_absent_port_warns_and_succeeds()
    {
        var store = new FakeConfigStore();

        var (exitCode, output, error) = await Run(
            VisaAsrlCommand.Build(Services(store)),
            ["add", "1", "COM3"]
        );

        exitCode.ShouldBe(0);
        output.ShouldContain("registered ASRL1::INSTR as COM3");
        error.ShouldContain("warning: COM3 is not present now");
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(
        Command command,
        string[] args
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exitCode = await command
                .Parse(args)
                .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static async Task<ConfigDocument> Saved(FakeConfigStore store) =>
        (await store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBeOk();

    private static ServiceProvider Services(FakeConfigStore store) =>
        new ServiceCollection()
            .AddSingleton<IConfigStore>(store)
            .AddSingleton<ISerialPorts>(new NoSerialPorts())
            .AddSingleton<AddDeviceCommandHandler>()
            .AddSingleton<AddAsrlBoardCommandHandler>()
            .AddSingleton<RemoveAsrlBoardCommandHandler>()
            .AddSingleton<ListAsrlBoardsQueryHandler>()
            .AddLogging()
            .BuildServiceProvider();

    /// <summary>A machine with no serial ports plugged in.</summary>
    private sealed class NoSerialPorts : ISerialPorts
    {
        public string Resolve(string port) => port;

        public bool Exists(string port) => false;
    }
}
