using System.CommandLine;
using IviCli.Cli.Commands;
using IviCli.Domain.Servers;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// What <c>mock serve</c> prints for a client to connect with, per type
/// (ADR 0056 §2), and the options it takes (§1).
/// </summary>
public sealed class MockServeCommandTests
{
    private static IviCli.Domain.Servers.Server ServerOf(ServerType type, int port, string bind = "127.0.0.1") =>
        new(
            ServerName.From("mock").ShouldBeOk(),
            type,
            IpAddress.From(bind).ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );

    [Theory]
    [InlineData(ServerType.Socket, 5025, "connect: TCPIP::127.0.0.1::5025::SOCKET")]
    [InlineData(ServerType.HiSlip, 4880, "connect: TCPIP::127.0.0.1::hislip0,4880::INSTR")]
    [InlineData(ServerType.Vxi11, 1024, "connect: TCPIP::127.0.0.1::inst0,1024::INSTR")]
    [InlineData(ServerType.UsbIp, 3240, "attach: usbip attach -r 127.0.0.1 -b 1-1")]
    [InlineData(ServerType.UsbIp, 3241, "attach: usbip --tcp-port 3241 attach -r 127.0.0.1 -b 1-1")]
    public void HowToConnect_names_the_resource_or_the_attach_step(
        ServerType type,
        int port,
        string expected
    )
    {
        MockServeCommand.HowToConnect(ServerOf(type, port)).ShouldBe(expected);
    }

    [Fact]
    public void HowToConnect_reaches_a_server_bound_to_every_address_at_loopback()
    {
        MockServeCommand
            .HowToConnect(ServerOf(ServerType.Socket, 5025, bind: "0.0.0.0"))
            .ShouldBe("connect: TCPIP::127.0.0.1::5025::SOCKET");
    }

    [Fact]
    public void HowToConnect_uses_a_specific_bind_address()
    {
        MockServeCommand
            .HowToConnect(ServerOf(ServerType.HiSlip, 4880, bind: "192.168.0.5"))
            .ShouldBe("connect: TCPIP::192.168.0.5::hislip0,4880::INSTR");
    }

    private static ParseResult Parse(string commandLine)
    {
        var root = new RootCommand("test root");
        root.Subcommands.Add(MockCommand.Build(new ServiceCollection().BuildServiceProvider()));
        return root.Parse(commandLine);
    }

    [Theory]
    [InlineData("mock serve my-dmm --type socket", true)]
    [InlineData(
        "mock serve my-dmm --type usbip --port 3241 --alias dmm --bind 0.0.0.0 --profile cdc-acm",
        true
    )]
    [InlineData("mock serve my-dmm", false)]
    public void Serve_requires_a_type(string commandLine, bool parses)
    {
        (Parse(commandLine).Errors.Count == 0).ShouldBe(parses);
    }
}
