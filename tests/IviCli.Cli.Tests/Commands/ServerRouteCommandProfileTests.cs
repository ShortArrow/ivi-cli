using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Cli.Commands;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// <c>server route add</c> is where an operator picks the USB profile a
/// USB/IP export presents (ADR 0049 §5), so the option is exercised the
/// way an operator uses it: parse a command line, run it, and read the
/// route back out of the configuration it wrote.
/// </summary>
public sealed class ServerRouteCommandProfileTests
{
    [Fact]
    public async Task Adding_a_route_without_the_option_exports_the_instrument_profile()
    {
        var store = Seeded();

        var exitCode = await RunAsync(store, "add", "usb-srv", "1-1", "dut");

        exitCode.ShouldBe(0);
        (await RouteOfAsync(store)).Profile.ShouldBe(UsbExportProfile.UsbTmc);
    }

    [Fact]
    public async Task Adding_a_route_with_the_serial_profile_records_it()
    {
        var store = Seeded();

        var exitCode = await RunAsync(
            store,
            "add",
            "usb-srv",
            "1-1",
            "dut",
            "--profile",
            "cdc-acm"
        );

        exitCode.ShouldBe(0);
        (await RouteOfAsync(store)).Profile.ShouldBe(UsbExportProfile.CdcAcm);
    }

    [Fact]
    public async Task An_unknown_profile_is_refused_and_nothing_is_written()
    {
        var store = Seeded();

        var exitCode = await RunAsync(store, "add", "usb-srv", "1-1", "dut", "--profile", "rs232");

        exitCode.ShouldNotBe(0);
        var config = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        config.Routes.ShouldBeEmpty();
    }

    private static async Task<int> RunAsync(FakeConfigStore store, params string[] args)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfigStore>(store)
            .AddSingleton<AddRouteCommandHandler>()
            .AddSingleton<RemoveRouteCommandHandler>()
            .AddSingleton<ListRoutesQueryHandler>()
            .AddLogging()
            .BuildServiceProvider();

        var command = ServerRouteCommand.Build(services);
        return await command.Parse(args).InvokeAsync(CancellationToken.None);
    }

    private static async Task<Route> RouteOfAsync(FakeConfigStore store)
    {
        var config = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        return config.Routes.ShouldHaveSingleItem();
    }

    private static FakeConfigStore Seeded()
    {
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var serverName = ServerName.From("usb-srv").ShouldBeOk();
        var config = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    deviceName,
                    VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddServer(
                new IviCli.Domain.Servers.Server(
                    serverName,
                    ServerType.UsbIp,
                    IpAddress.From("127.0.0.1").ShouldBeOk(),
                    Port.From(3240).ShouldBeOk()
                )
            )
            .ShouldBeOk();
        return new FakeConfigStore(config);
    }
}
