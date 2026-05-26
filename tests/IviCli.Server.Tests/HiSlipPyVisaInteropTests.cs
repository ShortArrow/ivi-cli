using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// Wire-level interop coverage: PyVISA (via the standard pyvisa-py
/// HiSLIP transport) drives an in-proc <see cref="HiSlipGatewayServer"/>
/// over loopback and reads back the response that the gateway routes to
/// <see cref="FakeBackend"/>. Demonstrates the new Requires/PrereqProbe
/// gate by running only when <c>python</c> + <c>pyvisa</c> are present.
/// </summary>
public sealed class HiSlipPyVisaInteropTests
{
    [Requires("python", "pyvisa")]
    [Trait("Category", "Integration")]
    public async Task PyVisa_can_idn_query_in_proc_hislip_gateway()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(5000).ShouldBeOk()
        );
        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("dut").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.HiSlip,
            bind,
            portValue
        );
        var route = new Route(serverName, endpoint, deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();

        var fake = new FakeBackend().RespondToQuery(deviceName, "*IDN?", "FAKE,HISLIP,IVI-CLI,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var (exitCode, stdout, stderr) = await RunPythonClientAsync(port, cts.Token);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }

        exitCode.ShouldBe(0, $"python client failed.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        stdout.Trim().ShouldBe("FAKE,HISLIP,IVI-CLI,1.0");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunPythonClientAsync(
        int port,
        CancellationToken ct
    )
    {
        var script =
            "import sys\n"
            + "import pyvisa\n"
            + "rm = pyvisa.ResourceManager('@py')\n"
            + $"inst = rm.open_resource('TCPIP0::127.0.0.1::hislip0,{port}::INSTR', open_timeout=5000)\n"
            + "inst.timeout = 5000\n"
            + "try:\n"
            + "    sys.stdout.write(inst.query('*IDN?').strip())\n"
            + "    sys.stdout.flush()\n"
            + "finally:\n"
            + "    inst.close()\n"
            + "    rm.close()\n";
        var python = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";
        var psi = new ProcessStartInfo
        {
            FileName = python,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);

        using var process =
            Process.Start(psi) ?? throw new InvalidOperationException("python failed to start");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForListenerAsync(int port, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, ct);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50, ct);
            }
        }
        throw new TimeoutException($"HiSLIP gateway did not bind to port {port}");
    }
}
