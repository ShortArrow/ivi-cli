using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Backends.Fake.Tests;

public class FakeBackendScenarioTests
{
    private static DeviceName DevName() => DeviceName.From("psu1").ShouldBeOk();

    private static Device Dev() =>
        new(
            DevName(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static MockScenario MakeScenario(params MockRule[] rules) =>
        MockScenario.SingleScene(
            ScenarioName.From("test-scn").ShouldBeOk(),
            idnDefault: "ACME,FAKE-PSU,001,1.0",
            rules: rules.ToImmutableArray()
        );

    [Fact]
    public async Task ActiveScenario_RespondScene_OverridesDefaultQuery()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(new MockRule("MEAS:VOLT?", new RuleAction.Respond("3.30"))),
            DevName()
        );

        // When
        var result = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("MEAS:VOLT?").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk().ShouldBe("3.30");
    }

    [Theory]
    [InlineData(":MEAS:VOLT?", "MEAS:VOLT?")] // client adds colon, scene without
    [InlineData("MEAS:VOLT?", ":MEAS:VOLT?")] // scene adds colon, client without
    [InlineData(":MEAS:VOLT?", ":MEAS:VOLT?")] // both prefixed
    [InlineData("MEAS:VOLT?", "MEAS:VOLT?")] // legacy exact match still works
    public async Task ActiveScenario_LeadingColon_Normalized(string clientScpi, string sceneMatch)
    {
        // Per SCPI 1999 §6.1.1 and IEEE 488.2 §7.5, the leading
        // colon is the "absolute path from root" prefix; at message
        // start there is no current path, so `:OUTP` ≡ `OUTP` at the
        // wire level. Real VISA clients (NI-VISA, Keysight, PyVISA,
        // ImageDataGetter via NI-VISA) emit the colon-prefixed form.
        // Scene lookup must honour both.
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(new MockRule(sceneMatch, new RuleAction.Respond("3.30"))),
            DevName()
        );

        var result = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From(clientScpi).ShouldBeOk(),
            CancellationToken.None
        );

        result.ShouldBeOk().ShouldBe("3.30");
    }

    [Fact]
    public async Task ActiveScenario_IdnDefault_UsedWhenNoExplicitScene()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(MakeScenario(), DevName());

        // When
        var result = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk().ShouldBe("ACME,FAKE-PSU,001,1.0");
    }

    [Fact]
    public async Task ActiveScenario_AckScene_AllowsWriteAndRejectsQuery()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(new MockRule("OUTP ON", new RuleAction.Ack())),
            DevName()
        );

        // When
        var writeResult = await backend.WriteAsync(
            Dev(),
            ScpiCommand.From("OUTP ON").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        writeResult.ShouldBeOk();
    }

    [Fact]
    public async Task ActiveScenario_RespondScene_OnWrite_ReturnsContractMismatch()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(new MockRule("OUTP ON", new RuleAction.Respond("ok"))),
            DevName()
        );

        // When
        var result = await backend.WriteAsync(
            Dev(),
            ScpiCommand.From("OUTP ON").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<MockScenarioContractMismatch>();
    }

    [Fact]
    public async Task ActiveScenario_AckScene_OnQuery_ReturnsContractMismatch()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(new MockRule("MEAS:VOLT?", new RuleAction.Ack())),
            DevName()
        );

        // When
        var result = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("MEAS:VOLT?").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<MockScenarioContractMismatch>();
    }

    [Fact]
    public async Task ActiveScenario_FailScene_MapsTransportTimeout()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(
                new MockRule("MEAS:VOLT?", new RuleAction.Fail("transport_timeout", "50"))
            ),
            DevName()
        );

        // When
        var result = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("MEAS:VOLT?").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<TransportTimeout>();
        err.Elapsed.TotalMilliseconds.ShouldBe(50);
    }

    [Fact]
    public async Task DeactivateScenario_RestoresDefaultBehavior()
    {
        // Given
        var backend = new FakeBackend();
        backend.ActivateScenario(
            MakeScenario(new MockRule("*IDN?", new RuleAction.Respond("FROM,SCENARIO"))),
            DevName()
        );
        var pre = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            CancellationToken.None
        );
        pre.ShouldBeOk().ShouldBe("FROM,SCENARIO");

        // When
        backend.DeactivateScenario(DevName());
        var post = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            CancellationToken.None
        );

        // Then
        post.ShouldBeOk().ShouldBe("FAKE,FAKE,0,1.0");
    }
}
