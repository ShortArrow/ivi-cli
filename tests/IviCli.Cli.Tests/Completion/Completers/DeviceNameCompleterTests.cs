using IviCli.Cli.Completion.Completers;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Completion.Completers;

public sealed class DeviceNameCompleterTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static FakeConfigStore StoreWith(params Device[] devices)
    {
        var doc = ConfigDocument.Empty;
        foreach (var d in devices)
        {
            doc = doc.AddDevice(d).ShouldBeOk();
        }
        return new FakeConfigStore(doc);
    }

    [Fact]
    public async Task CompleteAsync_returns_all_devices_when_prefix_empty()
    {
        var completer = new DeviceNameCompleter(StoreWith(Dev("psu1"), Dev("dmm1")));
        var candidates = await completer.CompleteAsync(string.Empty, default);
        string[] expected = ["dmm1", "psu1"];
        candidates.ShouldBe(expected);
    }

    [Fact]
    public async Task CompleteAsync_filters_by_prefix()
    {
        var completer = new DeviceNameCompleter(StoreWith(Dev("psu1"), Dev("dmm1"), Dev("psu2")));
        var candidates = await completer.CompleteAsync("psu", default);
        string[] expected = ["psu1", "psu2"];
        candidates.ShouldBe(expected);
    }
}
