using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Local;
using IviCli.Backends.Serial;
using IviCli.Backends.Socket;
using IviCli.Backends.Vxi11;
using IviCli.Cli;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests;

/// <summary>
/// The CLI's routing, over the backends its service registrations build,
/// with the VISA runtime and the serial ports replaced by recorders: an
/// ASRL port opens on the Serial backend, and an ASRL board on the Serial
/// backend when the <c>[[asrl]]</c> table registers it and through VISA
/// otherwise (ADR 0055 §2, §3).
/// </summary>
public sealed class TransportRoutingTests
{
    private readonly RecordingVisa _visa = new();
    private readonly RecordingSerialPorts _ports = new();

    [Fact]
    public async Task An_unregistered_board_opens_through_VISA()
    {
        await Open(Create(mockOnly: false), "ASRL1::INSTR");

        _visa.Opened.ShouldBe(["ASRL1::INSTR"]);
        _ports.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_registered_board_opens_its_port_on_the_serial_backend()
    {
        await Open(Create(mockOnly: false), "ASRL2::INSTR");

        _ports.Opened.ShouldBe(["COM7"]);
        _visa.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_named_port_opens_on_the_serial_backend()
    {
        await Open(Create(mockOnly: false), "ASRLCOM9::INSTR");

        _ports.Opened.ShouldBe(["COM9"]);
    }

    [Theory]
    [InlineData("ASRL1::INSTR")]
    [InlineData("ASRLCOM9::INSTR")]
    [InlineData("USB0::0x0699::0x0408::C012345::INSTR")]
    public void Mock_only_sends_every_device_to_the_fallback(string resource)
    {
        var fallback = new FakeBackend();

        var backend = TransportRouting
            .Create(Services(), fallback, mockOnly: true)
            .CreateFor(Device(resource))
            .ShouldBeOk();

        backend.ShouldBeSameAs(fallback);
    }

    private IBackendFactory Create(bool mockOnly) =>
        TransportRouting.Create(Services(), new FakeBackend(), mockOnly);

    private ServiceProvider Services()
    {
        var table = ConfigDocument
            .Empty.AddAsrlBoard(AsrlBoardRegistration.From(2, "COM7").ShouldBeOk())
            .ShouldBeOk();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddIviCliBackendsHiSlip();
        services.AddIviCliBackendsSocket();
        services.AddIviCliBackendsVxi11();
        services.AddIviCliBackendsLocal();
        services.AddIviCliBackendsSerial();
        services.AddSingleton<IVisaSessionFactory>(_visa);
        services.AddSingleton<ISerialLineFactory>(_ports);
        services.AddSingleton<IConfigStore>(new FakeConfigStore(table));
        return services.BuildServiceProvider();
    }

    private static async Task Open(IBackendFactory factory, string resource)
    {
        var device = Device(resource);
        var backend = factory.CreateFor(device).ShouldBeOk();
        await backend.OpenAsync(device, TestContext.Current.CancellationToken);
    }

    private static Device Device(string resource) =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    /// <summary>A VISA runtime that records what it was asked to open and opens nothing.</summary>
    private sealed class RecordingVisa : IVisaSessionFactory
    {
        public List<string> Opened { get; } = [];

        public Result<IVisaSessionHandle, LocalVisaError> Open(
            VisaResource resource,
            VisaSessionOptions options
        )
        {
            Opened.Add(VisaResourceFormatter.Format(resource));
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing("recorded")
            );
        }
    }

    /// <summary>Serial ports that record which port was opened and open nothing.</summary>
    private sealed class RecordingSerialPorts : ISerialLineFactory
    {
        public List<string> Opened { get; } = [];

        public ISerialLine Open(string port, SerialSettings settings, TimeSpan timeout)
        {
            Opened.Add(port);
            throw new SerialLineUnavailableException("recorded");
        }
    }
}
