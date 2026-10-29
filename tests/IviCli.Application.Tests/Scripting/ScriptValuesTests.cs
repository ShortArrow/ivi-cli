using IviCli.Application.Scripting;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Scripting;

/// <summary>
/// <c>!values</c> checks the most recent query response against its
/// pattern and stops the script at the first slot that refuses it
/// (ADR 0051 §5).
/// </summary>
public sealed class ScriptValuesTests
{
    [Fact]
    public async Task A_response_inside_the_pattern_lets_the_script_continue()
    {
        // Given
        var handler = Handler("25.3,52.1");

        // When
        var result = await handler.HandleAsync(
            new ScriptDeviceCommand(
                "dut",
                "MEAS:TEMP?\n!values {temp:20..30},{rh:40..60}\n!echo done"
            ),
            TestContext.Current.CancellationToken
        );

        // Then
        result.ShouldBeOk().Output.ShouldBe(["25.3,52.1", "done"]);
    }

    [Fact]
    public async Task A_response_outside_the_pattern_fails_naming_the_slot_and_the_response()
    {
        // Given
        var handler = Handler("25.3,72.1");

        // When
        var result = await handler.HandleAsync(
            new ScriptDeviceCommand("dut", "MEAS:TEMP?\n!values {temp:20..30},{rh:40..60}"),
            TestContext.Current.CancellationToken
        );

        // Then
        var failure = result.ShouldBeError().ShouldBeOfType<ScriptDeviceValuesFailure>();
        failure.Line.ShouldBe(2);
        failure.Pattern.ShouldBe("{temp:20..30},{rh:40..60}");
        failure.Reason.ShouldBe("slot 2 (rh) is 72.1, not in 40..60");
        failure.Actual.ShouldBe("25.3,72.1");
    }

    [Fact]
    public async Task Values_before_any_query_fails()
    {
        // Given
        var handler = Handler("25.3,52.1");

        // When
        var result = await handler.HandleAsync(
            new ScriptDeviceCommand("dut", "!values *"),
            TestContext.Current.CancellationToken
        );

        // Then
        var failure = result.ShouldBeError().ShouldBeOfType<ScriptDeviceValuesFailure>();
        failure.Reason.ShouldBe("no query has answered before this line");
    }

    private static ScriptDeviceCommandHandler Handler(string temperatureResponse)
    {
        var name = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            name,
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var fake = new FakeBackend().RespondToQuery(name, "MEAS:TEMP?", temperatureResponse);
        return new ScriptDeviceCommandHandler(
            new FakeConfigStore(ConfigDocument.Empty.AddDevice(device).ShouldBeOk()),
            new FakeSessionStore(),
            new FakeBackendFactory(fake)
        );
    }
}
