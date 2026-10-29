using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// A backend failure reaches a VXI-11 client as the call's error code
/// (ADR 0029 §5), and the gateway log says why.
/// </summary>
public sealed class Vxi11BackendFailureTests
{
    [Theory]
    [InlineData("OUTP OFF")]
    [InlineData("MEAS:CURR?")]
    public async Task Backend_failure_is_returned_to_the_client_and_logged(string request)
    {
        // Given
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            ServerName.From("srv").ShouldBeOk(),
            ServerType.Vxi11,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(LoopbackGateway.FreePort()).ShouldBeOk()
        );
        var config = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    deviceName,
                    VisaResource.Parse("TCPIP0::127.0.0.1::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(new Route(server.Name, PublicEndpoint.From("inst0").ShouldBeOk(), deviceName))
            .ShouldBeOk();
        var fake = new FakeBackend().ActivateScenario(
            MockScenario.SingleScene(
                ScenarioName.From("strict").ShouldBeOk(),
                idnDefault: null,
                rules: ImmutableArray<MockRule>.Empty
            ) with
            {
                Unmatched = UnmatchedPolicy.Fail,
            },
            deviceName
        );
        var logger = new RecordingLogger<Vxi11GatewayServer>();
        var gateway = new Vxi11GatewayServer(new FakeBackendFactory(fake), logger)
        {
            PortmapUdpPort = LoopbackGateway.FreePort(),
        };
        var (port, run) = LoopbackGateway.Start(
            server,
            s => gateway.RunAsync(s, config, cts.Token)
        );
        var client = new Vxi11Backend(port);
        var remote = new Device(
            DeviceName.From("remote").ShouldBeOk(),
            VisaResource.Parse($"TCPIP0::127.0.0.1::inst0,{port}::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        (await client.OpenAsync(remote, cts.Token)).ShouldBeOk();

        // When
        var failed = ScpiMessage.IsQuery(request)
            ? await client.QueryAsync(remote, ScpiQuery.From(request).ShouldBeOk(), cts.Token)
                is Result<string, BackendError>.Error
            : await client.WriteAsync(remote, ScpiCommand.From(request).ShouldBeOk(), cts.Token)
                is Result<Unit, BackendError>.Error;

        // Then
        failed.ShouldBeTrue();
        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Error && e.Message.Contains($"matches {request}")
        );

        await client.CloseAsync(remote, CancellationToken.None);
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException) { }
    }
}
