using IviCli.Application.Backends;
using IviCli.Backends.Serial;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Serial.Tests;

/// <summary>
/// The Serial backend against a fake line: what it writes, how it ends a
/// read, and how a port's failures become backend errors (ADR 0055 §4-§8).
/// </summary>
public sealed class SerialBackendTests
{
    private static Device Dev(string resource = "ASRL/dev/ttyUSB0::INSTR", int timeoutMs = 300) =>
        new(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_hands_the_port_settings_and_device_timeout_to_the_line()
    {
        var factory = new FakeLineFactory();
        var settings = SerialSettings
            .From(19200, 7, SerialParity.Even, SerialStopBits.Two, SerialFlowControl.RtsCts)
            .ShouldBeOk();
        var device = Dev() with { Serial = settings };

        (await new SerialBackend(factory).OpenAsync(device, Ct)).ShouldBeOk();

        factory.OpenedPort.ShouldBe("/dev/ttyUSB0");
        factory.OpenedSettings.ShouldBe(settings);
        factory.OpenedTimeout.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task A_device_without_serial_settings_opens_with_VISAs_defaults()
    {
        var factory = new FakeLineFactory();

        (await new SerialBackend(factory).OpenAsync(Dev(), Ct)).ShouldBeOk();

        factory.OpenedSettings.ShouldBe(SerialSettings.Default);
    }

    /// <summary>
    /// A board number needs the machine's <c>[[asrl]]</c> table, which backend
    /// selection consults (ADR 0055 §2); this backend opens ports by name.
    /// </summary>
    [Fact]
    public async Task A_board_resource_is_not_opened_here()
    {
        var result = await new SerialBackend(new FakeLineFactory()).OpenAsync(
            Dev("ASRL1::INSTR"),
            Ct
        );

        result.ShouldBeError().ShouldBeOfType<UnsupportedTransport>();
    }

    [Fact]
    public async Task A_port_another_program_holds_is_reported_as_in_use()
    {
        var factory = new FakeLineFactory { OpenFails = new UnauthorizedAccessException("busy") };

        var result = await new SerialBackend(factory).OpenAsync(Dev(), Ct);

        result.ShouldBeError().ShouldBeOfType<TransportInUse>().Device.Value.ShouldBe("dmm");
    }

    [Fact]
    public async Task A_port_that_cannot_be_opened_is_reported_as_disconnected()
    {
        var factory = new FakeLineFactory { OpenFails = new IOException("no such port") };

        var result = await new SerialBackend(factory).OpenAsync(Dev(), Ct);

        result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
    }

    /// <summary>
    /// A setting the port refuses fails the open and names the setting, but
    /// not with the exception's message, which can carry the port (ADR 0055 §4).
    /// </summary>
    [Fact]
    public async Task A_setting_the_port_refuses_fails_the_open_naming_it()
    {
        var factory = new FakeLineFactory
        {
            OpenFails = new ArgumentOutOfRangeException("StopBits", "1.5 is not supported"),
        };

        var result = await new SerialBackend(factory).OpenAsync(Dev(), Ct);

        result
            .ShouldBeError()
            .ShouldBeOfType<TransportDisconnected>()
            .Reason.ShouldContain("StopBits");
    }

    /// <summary>
    /// A port exception's message, like the exception, can carry the port's
    /// full name, which carries the adapter's serial number; logs mask it
    /// (ADR 0055 §1), so neither reaches the error.
    /// </summary>
    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("port-name")]
    public async Task A_failed_open_never_carries_the_port_name(string kind)
    {
        const string Secret = "/dev/serial/by-id/usb-FTDI_FT232R_A1B2C3-if00-port0";
        var factory = new FakeLineFactory
        {
            OpenFails = kind switch
            {
                "io" => new IOException($"Could not find file '{Secret}'"),
                "access" => new UnauthorizedAccessException($"Access to '{Secret}' is denied"),
#pragma warning disable CA2208 // Mimics SerialPort, which names its own portName parameter.
                _ => new ArgumentException($"The port name ({Secret}) is invalid", "portName"),
#pragma warning restore CA2208
            },
        };

        var result = await new SerialBackend(factory).OpenAsync(Dev($"ASRL{Secret}::INSTR"), Ct);

        var error = result.ShouldBeError();
        error.Cause.ShouldBeNull();
        string.Join(" ", error.LogArgs).ShouldNotContain("A1B2C3");
    }

    /// <summary>
    /// A refused port name is a port that cannot be opened, not a refused
    /// setting.
    /// </summary>
    [Fact]
    public async Task A_refused_port_name_is_not_reported_as_a_refused_setting()
    {
#pragma warning disable CA2208 // Mimics SerialPort, which names its own portName parameter.
        var factory = new FakeLineFactory
        {
            OpenFails = new ArgumentException("bad port", "portName"),
        };
#pragma warning restore CA2208

        var result = await new SerialBackend(factory).OpenAsync(Dev(), Ct);

        result
            .ShouldBeError()
            .ShouldBeOfType<TransportDisconnected>()
            .Reason.ShouldNotContain("refused");
    }

    /// <summary>
    /// A port the operating system names as missing, or as one the user may
    /// not open, says so in words the port name is not part of.
    /// </summary>
    [Fact]
    public async Task A_port_that_is_unavailable_says_why()
    {
        var factory = new FakeLineFactory
        {
            OpenFails = new SerialLineUnavailableException("the port does not exist"),
        };

        var result = await new SerialBackend(factory).OpenAsync(Dev(), Ct);

        result
            .ShouldBeError()
            .ShouldBeOfType<TransportDisconnected>()
            .Reason.ShouldContain("the port does not exist");
    }

    /// <summary>
    /// A port call that never returns is abandoned at the device timeout by
    /// the timeout decorator, which needs the call off the caller's thread
    /// and the wait to honour cancellation (ADR 0055 §8).
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task A_read_that_never_returns_is_abandoned_at_the_device_timeout()
    {
        var factory = new FakeLineFactory();
        using var release = new ManualResetEventSlim();
        factory.Line.ReadBlocksUntil = release;
        var device = Dev(timeoutMs: 200);
        var bounded = new DeviceTimeoutBackendFactory(
            new FakeBackendFactory(new SerialBackend(factory)),
            TimeProvider.System
        )
            .CreateFor(device)
            .ShouldBeOk();
        try
        {
            (await bounded.OpenAsync(device, TestContext.Current.CancellationToken)).ShouldBeOk();

            var read = bounded.ReadAsync(device, TestContext.Current.CancellationToken);
            var finished = await Task.WhenAny(
                read,
                Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            );

            finished.ShouldBe(read);
            (await read).ShouldBeError().ShouldBeOfType<TransportTimeout>();
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    /// An open the caller gave up on that completes later releases the port
    /// it opened: nothing else will, and a held port refuses every later
    /// open.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task An_open_given_up_on_releases_the_port_when_it_completes()
    {
        using var release = new ManualResetEventSlim();
        var factory = new FakeLineFactory { OpenBlocksUntil = release };
        var backend = new SerialBackend(factory);
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );

        var opening = backend.OpenAsync(Dev(), giveUp.Token);
        await giveUp.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => opening);
        release.Set();

        for (var i = 0; i < 100 && !factory.Line.Disposed; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        factory.Line.Disposed.ShouldBeTrue();
    }

    /// <summary>
    /// A port is exclusive, so opening a device again releases its line
    /// before opening the port anew.
    /// </summary>
    [Fact]
    public async Task Opening_an_open_device_again_releases_its_line_first()
    {
        var factory = new FakeLineFactory { Exclusive = true };
        var backend = new SerialBackend(factory);
        (await backend.OpenAsync(Dev(), Ct)).ShouldBeOk();

        var reopened = await backend.OpenAsync(Dev(), Ct);

        reopened.ShouldBeOk();
    }

    [Fact]
    public async Task A_line_failure_never_carries_the_port_name()
    {
        var (backend, line) = await Opened(Dev());
        line.ReadFails = new IOException("read failed on /dev/serial/by-id/usb-FTDI_A1B2C3");

        var result = await backend.ReadAsync(Dev(), Ct);

        var error = result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
        error.Cause.ShouldBeNull();
        error.Reason.ShouldNotContain("A1B2C3");
    }

    [Theory]
    [InlineData(null, "OUTP ON\n")]
    [InlineData("\r", "OUTP ON\r")]
    [InlineData("\r\n", "OUTP ON\r\n")]
    public async Task A_command_ends_with_the_write_terminator(string? terminator, string written)
    {
        var device = Dev() with
        {
            WriteTermination = terminator is null
                ? null
                : Termination.From(terminator).ShouldBeOk(),
        };
        var (backend, line) = await Opened(device);

        (
            await backend.WriteAsync(device, ScpiCommand.From("OUTP ON").ShouldBeOk(), Ct)
        ).ShouldBeOk();

        line.Written.ShouldBe([written]);
    }

    /// <summary>
    /// Without a read terminator a reply ends at LF and loses a trailing
    /// CR, as a read through VISA does (ADR 0055 §5).
    /// </summary>
    [Fact]
    public async Task Without_a_read_terminator_a_reply_ends_at_LF_without_its_CR()
    {
        var (backend, line) = await Opened(Dev());
        line.Replies.Enqueue("1.23\r");

        var result = await backend.QueryAsync(Dev(), ScpiQuery.From("MEAS?").ShouldBeOk(), Ct);

        result.ShouldBeOk().ShouldBe("1.23");
        line.Written.ShouldBe(["MEAS?\n"]);
        line.ReadUpTo.ShouldBe(["\n"]);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public async Task A_read_terminator_ends_a_reply_at_exactly_that_sequence(string terminator)
    {
        var device = Dev() with { ReadTermination = Termination.From(terminator).ShouldBeOk() };
        var (backend, line) = await Opened(device);
        line.Replies.Enqueue("1.23");

        var result = await backend.ReadAsync(device, Ct);

        result.ShouldBeOk().ShouldBe("1.23");
        line.ReadUpTo.ShouldBe([terminator]);
    }

    [Fact]
    public async Task A_read_the_line_times_out_on_is_a_transport_timeout()
    {
        var (backend, line) = await Opened(Dev(timeoutMs: 300));
        line.ReadFails = new TimeoutException();

        var result = await backend.ReadAsync(Dev(timeoutMs: 300), Ct);

        result
            .ShouldBeError()
            .ShouldBeOfType<TransportTimeout>()
            .Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task A_write_the_line_times_out_on_is_a_transport_timeout()
    {
        var (backend, line) = await Opened(Dev());
        line.WriteFails = new TimeoutException();

        var result = await backend.WriteAsync(Dev(), ScpiCommand.From("*RST").ShouldBeOk(), Ct);

        result.ShouldBeError().ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task A_line_that_fails_mid_read_is_reported_as_disconnected()
    {
        var (backend, line) = await Opened(Dev());
        line.ReadFails = new IOException("unplugged");

        var result = await backend.ReadAsync(Dev(), Ct);

        result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
    }

    [Fact]
    public async Task Trigger_writes_TRG()
    {
        var (backend, line) = await Opened(Dev());

        (await backend.TriggerAsync(Dev(), Ct)).ShouldBeOk();

        line.Written.ShouldBe(["*TRG\n"]);
    }

    [Fact]
    public async Task The_service_request_stream_ends_at_once()
    {
        var (backend, _) = await Opened(Dev());

        var count = 0;
        await foreach (var _ in backend.ServiceRequestStream(Dev(), Ct))
        {
            count++;
        }

        count.ShouldBe(0);
    }

    [Fact]
    public async Task An_operation_on_a_device_that_is_not_open_fails()
    {
        var backend = new SerialBackend(new FakeLineFactory());

        var result = await backend.WriteAsync(Dev(), ScpiCommand.From("*RST").ShouldBeOk(), Ct);

        result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
    }

    [Fact]
    public async Task Closing_releases_the_line()
    {
        var (backend, line) = await Opened(Dev());

        (await backend.CloseAsync(Dev(), Ct)).ShouldBeOk();

        line.Disposed.ShouldBeTrue();
        (
            await backend.WriteAsync(Dev(), ScpiCommand.From("*RST").ShouldBeOk(), Ct)
        ).ShouldBeError();
    }

    [Fact]
    public void The_serial_backend_enforces_the_device_timeout_itself() =>
        new SerialBackend(new FakeLineFactory()).ShouldBeAssignableTo<IEnforcesDeviceTimeout>();

    private static async Task<(SerialBackend Backend, FakeLine Line)> Opened(Device device)
    {
        var factory = new FakeLineFactory();
        var backend = new SerialBackend(factory);
        (await backend.OpenAsync(device, Ct)).ShouldBeOk();
        return (backend, factory.Line);
    }

    private sealed class FakeLineFactory : ISerialLineFactory
    {
        public FakeLine Line { get; private set; } = new();
        public Exception? OpenFails { get; init; }

        /// <summary>Refuse to open while the last line is still open, as an exclusive port does.</summary>
        public bool Exclusive { get; init; }

        private bool _opened;
        public string? OpenedPort { get; private set; }
        public SerialSettings? OpenedSettings { get; private set; }
        public TimeSpan? OpenedTimeout { get; private set; }

        /// <summary>Holds an open until set, as a slow driver might.</summary>
        public ManualResetEventSlim? OpenBlocksUntil { get; init; }

        public ISerialLine Open(string port, SerialSettings settings, TimeSpan timeout)
        {
            OpenBlocksUntil?.Wait();
            if (OpenFails is not null)
            {
                throw OpenFails;
            }
            if (Exclusive && _opened)
            {
                if (!Line.Disposed)
                {
                    throw new UnauthorizedAccessException("the port is in use");
                }
                Line = new FakeLine();
            }
            _opened = true;
            OpenedPort = port;
            OpenedSettings = settings;
            OpenedTimeout = timeout;
            return Line;
        }
    }

    private sealed class FakeLine : ISerialLine
    {
        public List<string> Written { get; } = [];
        public List<string> ReadUpTo { get; } = [];
        public Queue<string> Replies { get; } = new();
        public Exception? ReadFails { get; set; }
        public Exception? WriteFails { get; set; }
        public ManualResetEventSlim? ReadBlocksUntil { get; set; }
        public bool Disposed { get; private set; }

        public void Write(string text)
        {
            if (WriteFails is not null)
            {
                throw WriteFails;
            }
            Written.Add(text);
        }

        public string ReadTo(string terminator)
        {
            if (ReadBlocksUntil is { } release)
            {
                release.Wait();
                throw new TimeoutException();
            }
            if (ReadFails is not null)
            {
                throw ReadFails;
            }
            ReadUpTo.Add(terminator);
            return Replies.Dequeue();
        }

        public void Dispose() => Disposed = true;
    }
}
