using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.Infrastructure.Backends;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Backends;

/// <summary>
/// An <c>ASRL&lt;board&gt;::INSTR</c> device opens on the port the machine's
/// <c>[[asrl]]</c> table registers, or through VISA when the board is not
/// registered (ADR 0055 §2, §3).
/// </summary>
public sealed class AsrlBoardBackendTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Device Board1 =>
        new(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse("ASRL1::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static FakeConfigStore Registered(int board, string port) =>
        new(
            ConfigDocument
                .Empty.AddAsrlBoard(AsrlBoardRegistration.From(board, port).ShouldBeOk())
                .ShouldBeOk()
        );

    [Fact]
    public async Task A_registered_board_opens_its_port_on_the_serial_backend()
    {
        var serial = new RecordingBackend();
        var visa = new RecordingBackend();
        var backend = new AsrlBoardBackend(Registered(1, "/dev/ttyUSB0"), serial, visa);

        (await backend.OpenAsync(Board1, Ct)).ShouldBeOk();

        serial
            .Opened.ShouldHaveSingleItem()
            .Resource.ShouldBe(new VisaResource.AsrlPort("/dev/ttyUSB0"));
        visa.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unregistered_board_opens_through_VISA()
    {
        var serial = new RecordingBackend();
        var visa = new RecordingBackend();
        var backend = new AsrlBoardBackend(new FakeConfigStore(), serial, visa);

        (await backend.OpenAsync(Board1, Ct)).ShouldBeOk();

        visa.Opened.ShouldHaveSingleItem().ShouldBe(Board1);
        serial.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unregistered_board_VISA_cannot_open_points_at_visa_asrl_add()
    {
        var visa = new RecordingBackend { OpenFails = new TransportDisconnected("no runtime") };
        var backend = new AsrlBoardBackend(new FakeConfigStore(), new RecordingBackend(), visa);

        var result = await backend.OpenAsync(Board1, Ct);

        var error = result.ShouldBeError().ShouldBeOfType<AsrlBoardUnavailable>();
        error.Board.ShouldBe(1);
        error.Message.ShouldContain("visa asrl add");
    }

    [Fact]
    public async Task An_unregistered_board_with_no_VISA_points_at_visa_asrl_add()
    {
        var backend = new AsrlBoardBackend(new FakeConfigStore(), new RecordingBackend(), null);

        var result = await backend.OpenAsync(Board1, Ct);

        result.ShouldBeError().ShouldBeOfType<AsrlBoardUnavailable>().Board.ShouldBe(1);
    }

    [Fact]
    public async Task Operations_go_to_the_backend_that_opened_the_device()
    {
        var serial = new RecordingBackend();
        var visa = new RecordingBackend();
        var backend = new AsrlBoardBackend(Registered(1, "/dev/ttyUSB0"), serial, visa);
        (await backend.OpenAsync(Board1, Ct)).ShouldBeOk();

        (await backend.WriteAsync(Board1, ScpiCommand.From("*RST").ShouldBeOk(), Ct)).ShouldBeOk();
        (await backend.QueryAsync(Board1, ScpiQuery.From("*IDN?").ShouldBeOk(), Ct)).ShouldBeOk();
        (await backend.CloseAsync(Board1, Ct)).ShouldBeOk();

        serial.Calls.ShouldBe(["open", "write", "query", "close"]);
        visa.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// The table is read at each open, so a gateway that is already running
    /// sees a board registered after it started (ADR 0055 §3).
    /// </summary>
    [Fact]
    public async Task A_board_registered_after_a_session_closed_is_used_at_the_next_open()
    {
        var store = new FakeConfigStore();
        var serial = new RecordingBackend();
        var visa = new RecordingBackend();
        var backend = new AsrlBoardBackend(store, serial, visa);
        (await backend.OpenAsync(Board1, Ct)).ShouldBeOk();
        (await backend.CloseAsync(Board1, Ct)).ShouldBeOk();

        var config = (await store.LoadAsync(Ct)).ShouldBeOk();
        await store.SaveAsync(
            config.AddAsrlBoard(AsrlBoardRegistration.From(1, "COM3").ShouldBeOk()).ShouldBeOk(),
            Ct
        );
        (await backend.OpenAsync(Board1, Ct)).ShouldBeOk();

        serial.Opened.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_operation_before_the_device_is_open_fails()
    {
        var backend = new AsrlBoardBackend(
            new FakeConfigStore(),
            new RecordingBackend(),
            new RecordingBackend()
        );

        var result = await backend.WriteAsync(Board1, ScpiCommand.From("*RST").ShouldBeOk(), Ct);

        result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
    }

    [Fact]
    public void The_board_backend_enforces_the_device_timeout_itself() =>
        new AsrlBoardBackend(
            new FakeConfigStore(),
            new RecordingBackend(),
            null
        ).ShouldBeAssignableTo<IEnforcesDeviceTimeout>();

    /// <summary>A backend that records what it was asked to do and succeeds.</summary>
    private sealed class RecordingBackend : IIviBackend
    {
        public List<Device> Opened { get; } = [];
        public List<string> Calls { get; } = [];
        public BackendError? OpenFails { get; init; }

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
        {
            Calls.Add("open");
            if (OpenFails is not null)
            {
                return Task.FromResult(Result.Failure<Unit, BackendError>(OpenFails));
            }
            Opened.Add(device);
            return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
        }

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Done("close");

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Done("write");

        public Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        )
        {
            Calls.Add("query");
            return Task.FromResult(Result.Success<string, BackendError>("ACME"));
        }

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
        {
            Calls.Add("read");
            return Task.FromResult(Result.Success<string, BackendError>("ACME"));
        }

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Done("trigger");

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.CompletedTask;
            yield break;
        }

        private Task<Result<Unit, BackendError>> Done(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
        }
    }
}
