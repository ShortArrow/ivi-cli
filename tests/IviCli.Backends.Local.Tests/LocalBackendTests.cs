using System.Collections.Concurrent;
using IviCli.Application.Backends;
using IviCli.Backends.Local;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

public class LocalBackendTests
{
    private static Device Dev(string name, VisaResource? resource = null) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            resource ?? VisaResource.Parse("TCPIP0::192.168.1.10::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task OpenAsync_succeeds_when_factory_returns_handle()
    {
        var factory = new FakeVisaSessionFactory();
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = new FakeVisaSession();
        var backend = new LocalBackend(factory);

        var result = await backend.OpenAsync(Dev("psu"), default);
        result.ShouldBeOk();
    }

    [Fact]
    public async Task OpenAsync_fails_with_TransportDisconnected_when_runtime_missing()
    {
        var factory = new FakeVisaSessionFactory { ReturnRuntimeMissing = true };
        var backend = new LocalBackend(factory);

        var result = await backend.OpenAsync(Dev("psu"), default);
        result.ShouldBeOfType<Result<Unit, BackendError>.Error>();
    }

    [Fact]
    public async Task Runtime_missing_maps_to_a_reason_without_template_placeholders()
    {
        // Given a factory reporting the runtime as missing
        var factory = new FakeVisaSessionFactory { ReturnRuntimeMissing = true };
        var backend = new LocalBackend(factory);

        // When the open fails
        var result = await backend.OpenAsync(Dev("psu"), default);

        // Then the mapped reason is rendered text, never a raw {Placeholder}
        var err = ((Result<Unit, BackendError>.Error)result).Err;
        var reason = err.ShouldBeOfType<TransportDisconnected>().Reason;
        reason.ShouldNotContain("{");
        reason.ShouldContain("VISA runtime not available");
    }

    [Fact]
    public async Task WriteAsync_forwards_to_session_after_open()
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = session;
        var backend = new LocalBackend(factory);

        await backend.OpenAsync(Dev("psu"), default);
        var write = await backend.WriteAsync(
            Dev("psu"),
            ScpiCommand.From("*RST").ShouldBeOk(),
            default
        );

        write.ShouldBeOk();
        session.Writes.ShouldContain("*RST");
    }

    [Fact]
    public async Task QueryAsync_returns_response_from_session()
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        session.QueryResponses["*IDN?"] = "FAKE,FAKE,0,1.0";
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = session;
        var backend = new LocalBackend(factory);

        await backend.OpenAsync(Dev("psu"), default);
        var query = await backend.QueryAsync(
            Dev("psu"),
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            default
        );

        query.ShouldBeOk().ShouldBe("FAKE,FAKE,0,1.0");
    }

    [Fact]
    public async Task WriteAsync_returns_TransportDisconnected_when_session_not_open()
    {
        var factory = new FakeVisaSessionFactory();
        var backend = new LocalBackend(factory);

        var write = await backend.WriteAsync(
            Dev("psu"),
            ScpiCommand.From("*RST").ShouldBeOk(),
            default
        );
        write.ShouldBeOfType<Result<Unit, BackendError>.Error>();
    }

    [Fact]
    public async Task CloseAsync_disposes_handle_and_removes_session()
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = session;
        var backend = new LocalBackend(factory);

        await backend.OpenAsync(Dev("psu"), default);
        await backend.CloseAsync(Dev("psu"), default);

        session.Disposed.ShouldBeTrue();
        var write = await backend.WriteAsync(
            Dev("psu"),
            ScpiCommand.From("*RST").ShouldBeOk(),
            default
        );
        write.ShouldBeOfType<Result<Unit, BackendError>.Error>();
    }

    [Theory]
    [InlineData("TCPIP0::192.168.0.10::hislip0,5000::INSTR")]
    [InlineData("TCPIP0::192.168.0.10::gpib0,5::INSTR")]
    public void VisaResourceFormatter_keeps_the_lan_device_suffix(string resource)
    {
        VisaResourceFormatter.Format(VisaResource.Parse(resource).ShouldBeOk()).ShouldBe(resource);
    }

    [Fact]
    public void VisaResourceFormatter_round_trips_USB_with_interface()
    {
        var resource = VisaResource.Parse("USB0::0x0699::0x0408::C012345::1::INSTR").ShouldBeOk();
        VisaResourceFormatter.Format(resource).ShouldBe("USB0::0x0699::0x0408::C012345::1::INSTR");
    }

    [Fact]
    public void VisaResourceFormatter_round_trips_GPIB_with_secondary()
    {
        var resource = VisaResource.Parse("GPIB0::5::25::INSTR").ShouldBeOk();
        VisaResourceFormatter.Format(resource).ShouldBe("GPIB0::5::25::INSTR");
    }
}

internal sealed class FakeVisaSessionFactory : IVisaSessionFactory
{
    public ConcurrentDictionary<string, FakeVisaSession> Sessions { get; } = new();
    public bool ReturnRuntimeMissing { get; set; }

    public Result<IVisaSessionHandle, LocalVisaError> Open(VisaResource resource, TimeSpan timeout)
    {
        if (ReturnRuntimeMissing)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing("VISA runtime absent (test)")
            );
        }
        var key = VisaResourceFormatter.Format(resource);
        if (!Sessions.TryGetValue(key, out var session))
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaOpenFailure(key, "not configured in fake", null)
            );
        }
        return Result.Success<IVisaSessionHandle, LocalVisaError>(session);
    }
}

internal sealed class FakeVisaSession : IVisaSessionHandle
{
    private readonly TaskCompletionSource _enabled = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private Action<byte>? _onStatusByte;

    public List<string> Writes { get; } = new();
    public Dictionary<string, string> QueryResponses { get; } = new();
    public string? ReadResponse { get; set; }
    public bool Disposed { get; private set; }

    /// <summary>Makes <see cref="EnableServiceRequests"/> report an IO failure.</summary>
    public bool FailServiceRequestEnable { get; set; }

    /// <summary>Completes once a consumer has subscribed to service requests.</summary>
    public Task ServiceRequestsEnabled => _enabled.Task;

    /// <summary>Delivers <paramref name="statusByte"/> to the registered callback.</summary>
    public void RaiseServiceRequest(byte statusByte) => _onStatusByte?.Invoke(statusByte);

    public Result<Unit, LocalVisaError> Write(string text)
    {
        Writes.Add(text);
        return Result.Success<Unit, LocalVisaError>(Unit.Value);
    }

    public Result<string, LocalVisaError> Query(string text)
    {
        Writes.Add(text);
        if (!QueryResponses.TryGetValue(text, out var response))
        {
            return Result.Failure<string, LocalVisaError>(
                new LocalVisaIoFailure("no response configured", null)
            );
        }
        return Result.Success<string, LocalVisaError>(response);
    }

    public Result<string, LocalVisaError> Read()
    {
        if (ReadResponse is null)
        {
            return Result.Failure<string, LocalVisaError>(
                new LocalVisaIoFailure("no read configured", null)
            );
        }
        return Result.Success<string, LocalVisaError>(ReadResponse);
    }

    public Result<Unit, LocalVisaError> EnableServiceRequests(Action<byte> onStatusByte)
    {
        if (FailServiceRequestEnable)
        {
            return Result.Failure<Unit, LocalVisaError>(
                new LocalVisaIoFailure("service requests unavailable (test)", null)
            );
        }
        _onStatusByte = onStatusByte;
        _enabled.TrySetResult();
        return Result.Success<Unit, LocalVisaError>(Unit.Value);
    }

    public void Dispose() => Disposed = true;
}
