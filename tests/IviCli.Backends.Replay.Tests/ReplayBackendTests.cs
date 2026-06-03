using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Backends.Replay;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Replay.Tests;

public class ReplayBackendTests
{
    private static Device Dev() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static MockScenario Scenario(params MockRule[] rules) =>
        MockScenario.SingleScene(
            ScenarioName.From("demo").ShouldBeOk(),
            idnDefault: null,
            rules: rules.ToImmutableArray()
        );

    [Fact]
    public async Task QueryAsync_returns_response_from_matching_scene()
    {
        var scenario = Scenario(new MockRule("*IDN?", new RuleAction.Respond("FAKE,REPLAY,0,1")));
        var backend = new ReplayBackend(scenario);

        (await backend.OpenAsync(Dev(), default)).ShouldBeOk();
        var resp = await backend.QueryAsync(Dev(), ScpiQuery.From("*IDN?").ShouldBeOk(), default);
        resp.ShouldBeOk().ShouldBe("FAKE,REPLAY,0,1");
    }

    [Fact]
    public async Task QueryAsync_returns_ReplayMiss_when_no_scene_matches()
    {
        var backend = new ReplayBackend(Scenario());
        var resp = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("UNKNOWN?").ShouldBeOk(),
            default
        );
        resp.ShouldBeOfType<Result<string, BackendError>.Error>();
        ((Result<string, BackendError>.Error)resp).Err.ShouldBeOfType<ReplayMiss>();
    }

    [Fact]
    public async Task WriteAsync_accepts_Ack_scene()
    {
        var scenario = Scenario(new MockRule("*RST", new RuleAction.Ack()));
        var backend = new ReplayBackend(scenario);

        var write = await backend.WriteAsync(Dev(), ScpiCommand.From("*RST").ShouldBeOk(), default);
        write.ShouldBeOk();
    }

    [Fact]
    public async Task WriteAsync_returns_ActionMismatch_for_Respond_scene()
    {
        var scenario = Scenario(new MockRule("*RST", new RuleAction.Respond("oops")));
        var backend = new ReplayBackend(scenario);
        var write = await backend.WriteAsync(Dev(), ScpiCommand.From("*RST").ShouldBeOk(), default);
        write.ShouldBeOfType<Result<Unit, BackendError>.Error>();
        ((Result<Unit, BackendError>.Error)write).Err.ShouldBeOfType<ReplayActionMismatch>();
    }

    [Fact]
    public async Task QueryAsync_returns_CannedFailure_for_Fail_scene()
    {
        var scenario = Scenario(
            new MockRule("BROKEN?", new RuleAction.Fail("transport_timeout", "simulated"))
        );
        var backend = new ReplayBackend(scenario);
        var resp = await backend.QueryAsync(Dev(), ScpiQuery.From("BROKEN?").ShouldBeOk(), default);
        resp.ShouldBeOfType<Result<string, BackendError>.Error>();
        var fail = (
            (Result<string, BackendError>.Error)resp
        ).Err.ShouldBeOfType<ReplayCannedFailure>();
        fail.Variant.ShouldBe("transport_timeout");
    }

    [Fact]
    public async Task ReadAsync_always_misses_in_pure_replay()
    {
        var backend = new ReplayBackend(Scenario());
        var read = await backend.ReadAsync(Dev(), default);
        read.ShouldBeOfType<Result<string, BackendError>.Error>();
        ((Result<string, BackendError>.Error)read).Err.ShouldBeOfType<ReplayMiss>();
    }
}
