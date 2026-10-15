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

        var result = await backend.OpenAsync(Dev("psu"), TestContext.Current.CancellationToken);
        result.ShouldBeOk();
    }

    [Fact]
    public async Task A_missing_runtime_is_reported_as_such()
    {
        // Given a factory reporting the runtime as missing
        var factory = new FakeVisaSessionFactory { ReturnRuntimeMissing = true };
        var backend = new LocalBackend(factory);

        // When the open fails
        var result = await backend.OpenAsync(Dev("psu"), TestContext.Current.CancellationToken);

        // Then the error says the runtime is missing, with the runtime's detail
        result
            .ShouldBeError()
            .ShouldBeOfType<VisaRuntimeMissing>()
            .Detail.ShouldBe("VISA runtime absent (test)");
    }

    /// <summary>
    /// A resource the runtime does not know is told apart from other
    /// failures, so an ASRL board VISA has no port for can point at
    /// <c>visa asrl add</c> (ADR 0055 §2).
    /// </summary>
    [Theory]
    [InlineData(Ivi.Visa.NativeErrorCode.ResourceNotFound)]
    [InlineData(Ivi.Visa.NativeErrorCode.InvalidResourceName)]
    public async Task A_resource_the_runtime_does_not_know_is_not_found(int status)
    {
        var native = new Ivi.Visa.NativeVisaException(status);
        var factory = new FakeVisaSessionFactory { OpenFailsWith = native };
        var backend = new LocalBackend(factory);

        var result = await backend.OpenAsync(Dev("dmm"), TestContext.Current.CancellationToken);

        var notFound = result.ShouldBeError().ShouldBeOfType<ResourceNotFound>();
        notFound.Device.Value.ShouldBe("dmm");
        notFound.Cause.ShouldBeSameAs(native);
    }

    /// <summary>A resource another session holds is in use, as on the serial backend (ADR 0055 §7).</summary>
    [Theory]
    [InlineData(Ivi.Visa.NativeErrorCode.ResourceBusy)]
    [InlineData(Ivi.Visa.NativeErrorCode.ResourceLocked)]
    public async Task A_busy_resource_is_in_use(int status)
    {
        var native = new Ivi.Visa.NativeVisaException(status);
        var factory = new FakeVisaSessionFactory { OpenFailsWith = native };
        var backend = new LocalBackend(factory);

        var result = await backend.OpenAsync(Dev("dmm"), TestContext.Current.CancellationToken);

        result.ShouldBeError().ShouldBeOfType<TransportInUse>().Cause.ShouldBeSameAs(native);
    }

    /// <summary>
    /// A serial device opened through VISA gets its serial settings, VISA's
    /// defaults when it sets none, so it behaves as on the Serial backend
    /// whatever defaults the runtime was configured with (ADR 0055 §4).
    /// </summary>
    [Fact]
    public async Task A_serial_device_is_opened_with_its_serial_settings()
    {
        var settings = SerialSettings
            .From(19200, 7, SerialParity.Even, SerialStopBits.Two, SerialFlowControl.RtsCts)
            .ShouldBeOk();
        var device = Dev("dmm", Asrl) with { Serial = settings };
        var factory = Factory(device);

        (
            await new LocalBackend(factory).OpenAsync(device, TestContext.Current.CancellationToken)
        ).ShouldBeOk();

        factory.LastOptions!.Serial.ShouldBe(settings);
    }

    [Fact]
    public async Task A_serial_device_without_settings_is_opened_with_the_defaults()
    {
        var device = Dev("dmm", Asrl);
        var factory = Factory(device);

        (
            await new LocalBackend(factory).OpenAsync(device, TestContext.Current.CancellationToken)
        ).ShouldBeOk();

        factory.LastOptions!.Serial.ShouldBe(SerialSettings.Default);
    }

    [Fact]
    public async Task A_device_off_a_serial_line_gets_no_serial_settings()
    {
        var device = Dev("dmm");
        var factory = Factory(device);

        (
            await new LocalBackend(factory).OpenAsync(device, TestContext.Current.CancellationToken)
        ).ShouldBeOk();

        factory.LastOptions!.Serial.ShouldBeNull();
    }

    /// <summary>
    /// The terminators reach VISA only for SOCKET and serial; elsewhere
    /// the transport ends messages itself (ADR 0055 §5).
    /// </summary>
    [Theory]
    [InlineData("TCPIP0::host::5025::SOCKET", true)]
    [InlineData("ASRL1::INSTR", true)]
    [InlineData("TCPIP0::host::inst0::INSTR", false)]
    [InlineData("USB0::0x0699::0x0408::C012345::INSTR", false)]
    [InlineData("GPIB0::5::INSTR", false)]
    public async Task Terminators_reach_VISA_where_the_transport_uses_them(
        string resource,
        bool applied
    )
    {
        var device = Dev("dmm", VisaResource.Parse(resource).ShouldBeOk()) with
        {
            ReadTermination = Termination.CrLf,
            WriteTermination = Termination.Cr,
        };
        var factory = Factory(device);

        (
            await new LocalBackend(factory).OpenAsync(device, TestContext.Current.CancellationToken)
        ).ShouldBeOk();

        factory.LastOptions!.ReadTermination.ShouldBe(applied ? Termination.CrLf : null);
        factory.LastOptions.WriteTermination.ShouldBe(applied ? Termination.Cr : null);
    }

    /// <summary>
    /// A setting the serial driver refuses fails the open, named by its
    /// configuration name, as on the Serial backend (ADR 0055 §4).
    /// </summary>
    [Fact]
    public async Task A_refused_serial_setting_fails_the_open_naming_it()
    {
        var native = new Ivi.Visa.NativeVisaException(
            Ivi.Visa.NativeErrorCode.UnsupportedAttributeValue
        );
        var factory = new FakeVisaSessionFactory
        {
            RefusesSetting = new LocalVisaSettingRefused("baud", native),
        };
        var backend = new LocalBackend(factory);

        var result = await backend.OpenAsync(
            Dev("dmm", Asrl),
            TestContext.Current.CancellationToken
        );

        var error = result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
        error.Reason.ShouldBe("opening ASRL1::INSTR failed: the serial driver refused baud");
        error.Cause.ShouldBeSameAs(native);
    }

    private static readonly VisaResource Asrl = VisaResource.Parse("ASRL1::INSTR").ShouldBeOk();

    private static FakeVisaSessionFactory Factory(Device device)
    {
        var factory = new FakeVisaSessionFactory();
        factory.Sessions[VisaResourceFormatter.Format(device.Resource)] = new FakeVisaSession();
        return factory;
    }

    [Fact]
    public async Task WriteAsync_forwards_to_session_after_open()
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = session;
        var backend = new LocalBackend(factory);

        await backend.OpenAsync(Dev("psu"), TestContext.Current.CancellationToken);
        var write = await backend.WriteAsync(
            Dev("psu"),
            ScpiCommand.From("*RST").ShouldBeOk(),
            TestContext.Current.CancellationToken
        );

        write.ShouldBeOk();
        session.Writes.ShouldContain("*RST");
    }

    [Fact]
    [Spec(Standard.Visa, "§6.1.4")]
    public async Task TriggerAsync_writes_TRG_to_the_session()
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = session;
        var backend = new LocalBackend(factory);
        await backend.OpenAsync(Dev("psu"), TestContext.Current.CancellationToken);

        (
            await backend.TriggerAsync(Dev("psu"), TestContext.Current.CancellationToken)
        ).ShouldBeOk();

        session.Writes.ShouldBe(["*TRG"]);
    }

    [Fact]
    public async Task QueryAsync_returns_response_from_session()
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        session.QueryResponses["*IDN?"] = "FAKE,FAKE,0,1.0";
        factory.Sessions[VisaResourceFormatter.Format(Dev("psu").Resource)] = session;
        var backend = new LocalBackend(factory);

        await backend.OpenAsync(Dev("psu"), TestContext.Current.CancellationToken);
        var query = await backend.QueryAsync(
            Dev("psu"),
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            TestContext.Current.CancellationToken
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
            TestContext.Current.CancellationToken
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

        await backend.OpenAsync(Dev("psu"), TestContext.Current.CancellationToken);
        await backend.CloseAsync(Dev("psu"), TestContext.Current.CancellationToken);

        session.Disposed.ShouldBeTrue();
        var write = await backend.WriteAsync(
            Dev("psu"),
            ScpiCommand.From("*RST").ShouldBeOk(),
            TestContext.Current.CancellationToken
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

public class LocalBackendTimeoutTests
{
    private static Device Dev(int timeoutMs) =>
        new(
            DeviceName.From("psu").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.168.1.10::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    private static (
        LocalBackend Backend,
        FakeVisaSessionFactory Factory,
        FakeVisaSession Session
    ) Build(Device device)
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        factory.Sessions[VisaResourceFormatter.Format(device.Resource)] = session;
        return (new LocalBackend(factory), factory, session);
    }

    [Theory]
    [InlineData(300, 5000)]
    [InlineData(8000, 8000)]
    [Spec(Standard.Visa, "§5.1.2")]
    [Spec(Standard.Adr, "0053 §4")]
    public async Task The_session_gets_the_device_timeout_for_IO_and_at_least_five_seconds_to_open(
        int deviceTimeoutMs,
        int expectedOpenMs
    )
    {
        var device = Dev(deviceTimeoutMs);
        var (backend, factory, _) = Build(device);

        (await backend.OpenAsync(device, TestContext.Current.CancellationToken)).ShouldBeOk();

        factory.LastOptions!.IoTimeout.ShouldBe(TimeSpan.FromMilliseconds(deviceTimeoutMs));
        factory.LastOptions.OpenTimeout.ShouldBe(TimeSpan.FromMilliseconds(expectedOpenMs));
    }

    [Fact]
    [Spec(Standard.Visa, "§6.1.1")]
    [Spec(Standard.Visa, "§6.1.4")]
    public async Task A_VISA_timeout_is_reported_as_TransportTimeout()
    {
        var device = Dev(300);
        var (backend, _, session) = Build(device);
        session.AnswerAfter = System.Threading.Timeout.InfiniteTimeSpan;
        (await backend.OpenAsync(device, TestContext.Current.CancellationToken)).ShouldBeOk();

        var result = await backend.QueryAsync(
            device,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            TestContext.Current.CancellationToken
        );

        result
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>()
            .Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    /// <summary>
    /// Pins a deviation (#266): the VISA session keeps the I/O timeout it
    /// was opened with, so VISA cuts short an operation that carries a
    /// longer timeout, such as a VXI-11 gateway client's <c>io_timeout</c>.
    /// </summary>
    [Theory]
    [InlineData(Operation.Write)]
    [InlineData(Operation.Query)]
    [Spec(Standard.Vxi11, "B.4.3")]
    public async Task An_operation_carrying_a_longer_timeout_still_times_out_at_the_sessions(
        Operation operation
    )
    {
        var opened = Dev(300);
        var (backend, _, session) = Build(opened);
        session.QueryResponses["*IDN?"] = "ACME,PSU,0,1.0";
        session.AnswerAfter = TimeSpan.FromSeconds(1);
        (await backend.OpenAsync(opened, TestContext.Current.CancellationToken)).ShouldBeOk();

        var error = await Run(backend, Dev(5000), operation);

        error.ShouldBeOfType<TransportTimeout>().Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    /// <summary>
    /// Pins a deviation (#266): an operation that carries a shorter timeout
    /// than the session's still gets an answer that arrives after it.
    /// </summary>
    [Theory]
    [InlineData(Operation.Write)]
    [InlineData(Operation.Query)]
    [Spec(Standard.Vxi11, "B.4.3")]
    public async Task An_operation_carrying_a_shorter_timeout_still_gets_a_late_answer(
        Operation operation
    )
    {
        var opened = Dev(5000);
        var (backend, _, session) = Build(opened);
        session.QueryResponses["*IDN?"] = "ACME,PSU,0,1.0";
        session.AnswerAfter = TimeSpan.FromSeconds(1);
        (await backend.OpenAsync(opened, TestContext.Current.CancellationToken)).ShouldBeOk();

        var error = await Run(backend, Dev(300), operation);

        error.ShouldBeNull();
    }

    /// <summary>The operations a VXI-11 gateway client's <c>device_write</c> becomes.</summary>
    public enum Operation
    {
        Write,
        Query,
    }

    /// <summary>
    /// Runs <paramref name="operation"/> on <paramref name="device"/>.
    /// </summary>
    /// <returns>The operation's error, or <see langword="null"/> when it succeeds.</returns>
    private static async Task<BackendError?> Run(
        LocalBackend backend,
        Device device,
        Operation operation
    )
    {
        var ct = TestContext.Current.CancellationToken;
        return operation switch
        {
            Operation.Write => (
                await backend.WriteAsync(device, ScpiCommand.From("*RST").ShouldBeOk(), ct)
            )
                is Result<Unit, BackendError>.Error { Err: var err }
                ? err
                : null,
            _ => (await backend.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), ct))
                is Result<string, BackendError>.Error { Err: var err }
                ? err
                : null,
        };
    }

    [Fact]
    [Spec(Standard.Adr, "0053 §2")]
    public void The_local_backend_enforces_the_device_timeout_itself() =>
        new LocalBackend(
            new FakeVisaSessionFactory()
        ).ShouldBeAssignableTo<IEnforcesDeviceTimeout>();
}

internal sealed class FakeVisaSessionFactory : IVisaSessionFactory
{
    public ConcurrentDictionary<string, FakeVisaSession> Sessions { get; } = new();
    public bool ReturnRuntimeMissing { get; set; }

    /// <summary>Makes every open fail as the runtime does, with this exception inside.</summary>
    public Exception? OpenFailsWith { get; set; }

    /// <summary>Makes every open fail as a serial driver refusing a setting does.</summary>
    public LocalVisaSettingRefused? RefusesSetting { get; set; }

    public VisaSessionOptions? LastOptions { get; private set; }

    public Result<IVisaSessionHandle, LocalVisaError> Open(
        VisaResource resource,
        VisaSessionOptions options
    )
    {
        LastOptions = options;
        if (RefusesSetting is not null)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(RefusesSetting);
        }
        if (ReturnRuntimeMissing)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing("VISA runtime absent (test)")
            );
        }
        var key = VisaResourceFormatter.Format(resource);
        if (OpenFailsWith is not null)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaOpenFailure(key, OpenFailsWith.Message, OpenFailsWith)
            );
        }
        if (!Sessions.TryGetValue(key, out var session))
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaOpenFailure(key, "not configured in fake", null)
            );
        }
        session.IoTimeout = options.IoTimeout;
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

    /// <summary>
    /// The session's I/O timeout (<c>VI_ATTR_TMO_VALUE</c>), which the
    /// factory sets when it opens the session.
    /// </summary>
    public TimeSpan IoTimeout { get; set; }

    /// <summary>
    /// How long the instrument takes to accept a write or answer a query.
    /// Like VISA, the session reports a timeout instead when that is longer
    /// than <see cref="IoTimeout"/>.
    /// </summary>
    public TimeSpan AnswerAfter { get; set; }

    private bool TimesOut =>
        AnswerAfter == System.Threading.Timeout.InfiniteTimeSpan || AnswerAfter > IoTimeout;

    /// <summary>Makes <see cref="EnableServiceRequests"/> report an IO failure.</summary>
    public bool FailServiceRequestEnable { get; set; }

    /// <summary>Completes once a consumer has subscribed to service requests.</summary>
    public Task ServiceRequestsEnabled => _enabled.Task;

    /// <summary>Delivers <paramref name="statusByte"/> to the registered callback.</summary>
    public void RaiseServiceRequest(byte statusByte) => _onStatusByte?.Invoke(statusByte);

    public Result<Unit, LocalVisaError> Write(string text)
    {
        Writes.Add(text);
        return TimesOut
            ? Result.Failure<Unit, LocalVisaError>(new LocalVisaTimeout(IoTimeout, null))
            : Result.Success<Unit, LocalVisaError>(Unit.Value);
    }

    public Result<string, LocalVisaError> Query(string text)
    {
        Writes.Add(text);
        if (TimesOut)
        {
            return Result.Failure<string, LocalVisaError>(new LocalVisaTimeout(IoTimeout, null));
        }
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
