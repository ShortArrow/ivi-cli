using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Backends.Fake.Tests;

public class FakeBackendTests
{
    private static Device Dev(string name = "psu1") =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task OpenAsync_ByDefault_Succeeds()
    {
        // Given
        var backend = new FakeBackend();

        // When
        var result = await backend.OpenAsync(Dev(), CancellationToken.None);

        // Then
        result.ShouldBeOk();
    }

    [Fact]
    public async Task OpenAsync_WhenConfiguredToFail_ReturnsFailureOnce()
    {
        // Given
        var device = Dev();
        var backend = new FakeBackend();
        backend.FailNextOpen(device.Name, new TransportDisconnected("offline"));

        // When
        var first = await backend.OpenAsync(device, CancellationToken.None);
        var second = await backend.OpenAsync(device, CancellationToken.None);

        // Then
        first.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
        second.ShouldBeOk();
    }

    [Fact]
    public async Task QueryAsync_IdnByDefault_ReturnsFakeIdn()
    {
        // Given
        var backend = new FakeBackend();
        var query = ScpiQuery.From("*IDN?").ShouldBeOk();

        // When
        var result = await backend.QueryAsync(Dev(), query, CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldContain("FAKE");
    }

    [Fact]
    public async Task QueryAsync_AfterConfigureDevice_ReturnsCustomIdn()
    {
        // Given
        var device = Dev();
        var backend = new FakeBackend();
        backend.ConfigureDevice(device.Name, "KIKUSUI,PWR801L,001,1.0");
        var query = ScpiQuery.From("*IDN?").ShouldBeOk();

        // When
        var result = await backend.QueryAsync(device, query, CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldBe("KIKUSUI,PWR801L,001,1.0");
    }

    [Fact]
    public async Task QueryAsync_WithProgrammedResponse_ReturnsIt()
    {
        // Given
        var device = Dev();
        var backend = new FakeBackend();
        backend.RespondToQuery(device.Name, "MEAS:VOLT?", "3.30");
        var query = ScpiQuery.From("MEAS:VOLT?").ShouldBeOk();

        // When
        var result = await backend.QueryAsync(device, query, CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldBe("3.30");
    }

    [Fact]
    public async Task QueryAsync_WithScheduledFailure_ReturnsFailureOnce()
    {
        // Given
        var device = Dev();
        var backend = new FakeBackend();
        backend.FailQuery(
            device.Name,
            "MEAS:VOLT?",
            new TransportTimeout(TimeSpan.FromMilliseconds(50))
        );
        var query = ScpiQuery.From("MEAS:VOLT?").ShouldBeOk();

        // When
        var first = await backend.QueryAsync(device, query, CancellationToken.None);
        var second = await backend.QueryAsync(device, query, CancellationToken.None);

        // Then
        first.ShouldBeError().ShouldBeOfType<TransportTimeout>();
        second.ShouldBeOk(); // falls back to echo after fault is consumed
    }

    [Fact]
    public async Task WriteThenRead_RoundtripsTheLastWrittenCommand()
    {
        // Given
        var device = Dev();
        var backend = new FakeBackend();
        var command = ScpiCommand.From("OUTP ON").ShouldBeOk();

        // When
        (await backend.WriteAsync(device, command, CancellationToken.None)).ShouldBeOk();
        var read = await backend.ReadAsync(device, CancellationToken.None);

        // Then
        read.ShouldBeOk().ShouldBe("OUTP ON");
    }

    [Fact]
    public async Task QueryAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        // Given
        var backend = new FakeBackend();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var query = ScpiQuery.From("*IDN?").ShouldBeOk();

        // When / Then
        await Should.ThrowAsync<OperationCanceledException>(() =>
            backend.QueryAsync(Dev(), query, cts.Token)
        );
    }
}
