using System.Collections.Immutable;
using IviCli.Application.Capture;
using IviCli.Application.Mock;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Mock;

public sealed class TrafficScenarioConverterTests
{
    private static readonly DateTimeOffset T = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static TrafficEvent E(
        TrafficOp op,
        string device = "psu1",
        string? data = null,
        string? response = null,
        bool ok = true
    ) => new(T, device, op, data, response, ok, ok ? 5 : null, ok ? null : "boom");

    private static ScenarioName Name(string raw) => ScenarioName.From(raw).ShouldBeOk();

    private static DeviceName Dev(string raw) => DeviceName.From(raw).ShouldBeOk();

    [Fact]
    public void Convert_maps_Write_to_Ack_and_Query_to_Respond()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[]
        {
            E(TrafficOp.Write, data: "OUTP ON"),
            E(TrafficOp.Query, data: "*IDN?", response: "ACME,PSU,1,1.0"),
        };

        var scenario = conv.Convert(events, Name("psu1-smoke"), deviceFilter: null).ShouldBeOk();

        scenario.Scenes.Length.ShouldBe(2);
        scenario.Scenes[0].Match.ShouldBe("OUTP ON");
        scenario.Scenes[0].Action.ShouldBeOfType<SceneAction.Ack>();
        scenario.Scenes[1].Match.ShouldBe("*IDN?");
        var respond = scenario.Scenes[1].Action.ShouldBeOfType<SceneAction.Respond>();
        respond.Text.ShouldBe("ACME,PSU,1,1.0");
        scenario.IdnDefault.ShouldBe("ACME,PSU,1,1.0");
    }

    [Fact]
    public void Convert_returns_MultipleDevices_when_unfiltered_capture_spans_two_devices()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[]
        {
            E(TrafficOp.Query, device: "psu1", data: "*IDN?", response: "A"),
            E(TrafficOp.Query, device: "dmm1", data: "*IDN?", response: "B"),
        };

        var err = conv.Convert(events, Name("x"), deviceFilter: null)
            .ShouldBeError()
            .ShouldBeOfType<ConvertTrafficMultipleDevices>();

        err.Devices.ShouldBe(["dmm1", "psu1"]);
    }

    [Fact]
    public void Convert_with_deviceFilter_only_includes_matching_events()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[]
        {
            E(TrafficOp.Query, device: "psu1", data: "*IDN?", response: "A"),
            E(TrafficOp.Query, device: "dmm1", data: "*IDN?", response: "B"),
            E(TrafficOp.Write, device: "psu1", data: "OUTP ON"),
        };

        var scenario = conv.Convert(events, Name("psu1-only"), deviceFilter: Dev("psu1"))
            .ShouldBeOk();

        scenario.Scenes.Length.ShouldBe(2);
        scenario.Scenes.ShouldContain(s => s.Match == "*IDN?");
        scenario.Scenes.ShouldContain(s => s.Match == "OUTP ON");
        scenario.IdnDefault.ShouldBe("A");
    }

    [Fact]
    public void Convert_skips_failed_events()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[]
        {
            E(TrafficOp.Query, data: "*IDN?", response: "A"),
            E(TrafficOp.Query, data: "BOGUS?", ok: false),
        };

        var scenario = conv.Convert(events, Name("x"), deviceFilter: null).ShouldBeOk();

        scenario.Scenes.Length.ShouldBe(1);
        scenario.Scenes[0].Match.ShouldBe("*IDN?");
    }

    [Fact]
    public void Convert_skips_Open_Close_and_Read_events()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[]
        {
            E(TrafficOp.Open),
            E(TrafficOp.Read, response: "ignored"),
            E(TrafficOp.Query, data: "*IDN?", response: "A"),
            E(TrafficOp.Close),
        };

        var scenario = conv.Convert(events, Name("x"), deviceFilter: null).ShouldBeOk();

        scenario.Scenes.Length.ShouldBe(1);
    }

    [Fact]
    public void Convert_returns_NoScenes_when_filter_excludes_everything()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[] { E(TrafficOp.Query, device: "psu1", data: "*IDN?", response: "A") };

        var err = conv.Convert(events, Name("x"), deviceFilter: Dev("dmm1"))
            .ShouldBeError()
            .ShouldBeOfType<ConvertTrafficNoScenes>();

        err.DeviceFilter.ShouldBe("dmm1");
    }

    [Fact]
    public void Convert_IdnDefault_is_first_Query_response_for_IDN()
    {
        var conv = new DefaultTrafficScenarioConverter();
        var events = new[]
        {
            E(TrafficOp.Write, data: "*RST"),
            E(TrafficOp.Query, data: "*IDN?", response: "FIRST"),
            E(TrafficOp.Query, data: "*IDN?", response: "SECOND"),
        };

        var scenario = conv.Convert(events, Name("x"), deviceFilter: null).ShouldBeOk();

        scenario.IdnDefault.ShouldBe("FIRST");
        scenario.Scenes.Length.ShouldBe(3);
    }
}
