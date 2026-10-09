using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11.Tests;

/// <summary>
/// Unit tests for <see cref="Vxi11Backend"/> driving a hand-rolled stub
/// listener. The stub keeps the test focused on the client's RPC framing
/// and reply-decoding behaviour rather than reusing the real gateway —
/// that pairing is exercised by the end-to-end test in Task 3.
/// </summary>
public sealed class Vxi11BackendTests
{
    /// <summary>
    /// After Batch Q OpenAsync issues device_create_intr_chan +
    /// device_enable_srq right after create_link. Test stubs that don't
    /// drive SRQ semantics call this helper to drain + ack both RPCs.
    /// </summary>
    private static void AckInterruptSetup(StubSession session)
    {
        var createIntr = session.ReadCall();
        createIntr.Procedure.ShouldBe(ProcCreateIntrChan);
        session.WriteReply(createIntr.Xid, w => w.WriteInt32(Vxi11NoError));
        var enableSrq = session.ReadCall();
        enableSrq.Procedure.ShouldBe(ProcDeviceEnableSrq);
        session.WriteReply(enableSrq.Xid, w => w.WriteInt32(Vxi11NoError));
    }

    [Fact]
    public async Task OpenAsync_succeeds_when_stub_returns_no_error()
    {
        await using var stub = await StubServer.StartAsync(programmer: session =>
        {
            // create_link
            var call = session.ReadCall();
            call.Procedure.ShouldBe(ProcCreateLink);
            session.WriteReply(
                call.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteInt32(42); // lid
                    writer.WriteUInt32(0); // abort port
                    writer.WriteUInt32(16 * 1024 * 1024); // maxRecvSize
                }
            );
            AckInterruptSetup(session);
        });

        var backend = new Vxi11Backend(stub.Port);
        var device = BuildDevice();

        var result = await backend.OpenAsync(device, default);

        result.ShouldBeOk();
        await stub.WaitForClientAsync();
    }

    [Fact]
    public async Task QueryAsync_writes_then_reads_and_returns_response_text()
    {
        await using var stub = await StubServer.StartAsync(programmer: session =>
        {
            var create = session.ReadCall();
            session.WriteReply(
                create.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteInt32(7);
                    writer.WriteUInt32(0);
                    writer.WriteUInt32(16 * 1024 * 1024);
                }
            );
            AckInterruptSetup(session);

            var write = session.ReadCall();
            write.Procedure.ShouldBe(ProcDeviceWrite);
            session.WriteReply(
                write.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteUInt32(8); // bytes accepted
                }
            );

            var read = session.ReadCall();
            read.Procedure.ShouldBe(ProcDeviceRead);
            session.WriteReply(
                read.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteInt32(ReadReasonEnd);
                    writer.WriteOpaque("FAKE,IDN\n"u8.ToArray());
                }
            );
        });

        var backend = new Vxi11Backend(stub.Port);
        var device = BuildDevice();
        (await backend.OpenAsync(device, default)).ShouldBeOk();

        var query = ScpiQuery.From("*IDN?").ShouldBeOk();
        var result = await backend.QueryAsync(device, query, default);
        result.ShouldBeOk().ShouldBe("FAKE,IDN");

        await stub.WaitForClientAsync();
    }

    [Fact]
    public async Task WriteAsync_returns_failure_when_server_reports_error()
    {
        await using var stub = await StubServer.StartAsync(programmer: session =>
        {
            var create = session.ReadCall();
            session.WriteReply(
                create.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteInt32(1);
                    writer.WriteUInt32(0);
                    writer.WriteUInt32(16 * 1024 * 1024);
                }
            );
            AckInterruptSetup(session);
            var write = session.ReadCall();
            session.WriteReply(
                write.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11IoError);
                    writer.WriteUInt32(0);
                }
            );
        });

        var backend = new Vxi11Backend(stub.Port);
        var device = BuildDevice();
        (await backend.OpenAsync(device, default)).ShouldBeOk();

        var command = ScpiCommand.From("OUTP ON").ShouldBeOk();
        var result = await backend.WriteAsync(device, command, default);

        result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
    }

    [Fact]
    public async Task OpenAsync_returns_failure_for_non_inst_LanDevice()
    {
        var backend = new Vxi11Backend(12345);
        var device = new Device(
            DeviceName.From("d1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

        var result = await backend.OpenAsync(device, default);

        result.ShouldBeError().ShouldBeOfType<TransportDisconnected>();
    }

    [Fact]
    public async Task Write_read_and_trigger_send_the_device_timeout_as_io_timeout()
    {
        var ioTimeouts = new List<uint>();
        await using var stub = await StubServer.StartAsync(programmer: session =>
        {
            AckCreateLink(session);
            AckInterruptSetup(session);
            var write = session.ReadCall();
            ioTimeouts.Add(write.Word(1)); // lid, io_timeout, ...
            session.WriteReply(
                write.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteUInt32(6);
                }
            );
            var read = session.ReadCall();
            ioTimeouts.Add(read.Word(2)); // lid, requestSize, io_timeout, ...
            session.WriteReply(
                read.Xid,
                writer =>
                {
                    writer.WriteInt32(Vxi11NoError);
                    writer.WriteInt32(ReadReasonEnd);
                    writer.WriteOpaque("1\n"u8.ToArray());
                }
            );
            var trigger = session.ReadCall();
            trigger.Procedure.ShouldBe(ProcDeviceTrigger);
            ioTimeouts.Add(trigger.Word(2)); // lid, flags, io_timeout, ...
            session.WriteReply(trigger.Xid, writer => writer.WriteInt32(Vxi11NoError));
        });
        var backend = new Vxi11Backend(stub.Port);
        var device = BuildDevice(timeoutMs: 1234);
        (await backend.OpenAsync(device, default)).ShouldBeOk();

        (
            await backend.QueryAsync(device, ScpiQuery.From("*OPC?").ShouldBeOk(), default)
        ).ShouldBeOk();
        (await backend.TriggerAsync(device, default)).ShouldBeOk();

        await stub.WaitForClientAsync();
        ioTimeouts.ShouldBe([1234u, 1234u, 1234u]);
    }

    [Fact]
    public async Task A_close_cut_short_still_releases_the_connection()
    {
        await using var stub = await StubServer.StartAsync(programmer: session =>
        {
            AckCreateLink(session);
            AckInterruptSetup(session);
            session.WaitForHangUp();
        });
        var backend = new Vxi11Backend(stub.Port);
        var device = BuildDevice();
        (await backend.OpenAsync(device, default)).ShouldBeOk();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await backend.CloseAsync(device, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException) { }

        await stub.WaitForClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void AckCreateLink(StubSession session)
    {
        var create = session.ReadCall();
        create.Procedure.ShouldBe(ProcCreateLink);
        session.WriteReply(
            create.Xid,
            writer =>
            {
                writer.WriteInt32(Vxi11NoError);
                writer.WriteInt32(1); // lid
                writer.WriteUInt32(0); // abort port
                writer.WriteUInt32(16 * 1024 * 1024); // maxRecvSize
            }
        );
    }

    [Fact]
    public void The_VXI11_backend_enforces_the_device_timeout_itself() =>
        new Vxi11Backend(12345).ShouldBeAssignableTo<IEnforcesDeviceTimeout>();

    [Fact]
    public async Task CloseAsync_is_noop_when_session_was_never_opened()
    {
        var backend = new Vxi11Backend(12345);
        var device = BuildDevice();

        var result = await backend.CloseAsync(device, default);

        result.ShouldBeOk();
    }

    private static Device BuildDevice(int timeoutMs = 3000) =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    private sealed class StubServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _runner;
        private readonly TaskCompletionSource _clientDone = new();

        private StubServer(TcpListener listener, Task runner)
        {
            _listener = listener;
            _runner = runner;
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<StubServer> StartAsync(Action<StubSession> programmer)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var tcs = new TaskCompletionSource<StubServer>();
            StubServer server = null!;
            var runner = Task.Run(async () =>
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    using var session = new StubSession(client);
                    programmer(session);
                    server.CompleteClient();
                }
                catch (Exception ex)
                {
                    server.FailClient(ex);
                }
            });
            server = new StubServer(listener, runner);
            tcs.SetResult(server);
            return tcs.Task;
        }

        public Task WaitForClientAsync() => _clientDone.Task;

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }

        private void CompleteClient() => _clientDone.TrySetResult();

        private void FailClient(Exception ex) => _clientDone.TrySetException(ex);
    }

    private sealed class StubSession : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public StubSession(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public StubCall ReadCall()
        {
            var bytes = Vxi11RecordFraming
                .ReadRecordAsync(_stream, default)
                .GetAwaiter()
                .GetResult();
            var reader = new Vxi11XdrCodec.XdrReader(bytes);
            var xid = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // mtype = CALL
            _ = reader.ReadUInt32(); // rpcvers
            _ = reader.ReadUInt32(); // prog
            _ = reader.ReadUInt32(); // vers
            var proc = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // cred flavor
            _ = reader.ReadOpaque(); // cred body
            _ = reader.ReadUInt32(); // verf flavor
            _ = reader.ReadOpaque(); // verf body
            return new StubCall(xid, proc, bytes.AsMemory(reader.Position));
        }

        /// <summary>
        /// Reads calls without answering them until the client closes the
        /// connection.
        /// </summary>
        public void WaitForHangUp()
        {
            try
            {
                while (true)
                {
                    _ = ReadCall();
                }
            }
            catch (Exception ex)
                when (ex is IOException or EndOfStreamException or InvalidDataException) { }
        }

        public void WriteReply(uint xid, Action<Vxi11XdrCodec.XdrWriter> body)
        {
            var writer = new Vxi11XdrCodec.XdrWriter();
            writer.WriteUInt32(xid);
            writer.WriteUInt32(1); // mtype = REPLY
            writer.WriteUInt32(MsgAccepted);
            writer.WriteUInt32(0); // verf flavor
            writer.WriteOpaque([]); // verf body
            writer.WriteUInt32(AcceptSuccess);
            body(writer);
            Vxi11RecordFraming
                .WriteRecordAsync(_stream, writer.ToArray(), default)
                .GetAwaiter()
                .GetResult();
        }

        public void Dispose()
        {
            _stream.Dispose();
            _client.Dispose();
        }
    }

    private readonly record struct StubCall(uint Xid, uint Procedure, ReadOnlyMemory<byte> Args)
    {
        /// <summary>The XDR word at <paramref name="index"/> of the call's arguments.</summary>
        public uint Word(int index) =>
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                Args.Span.Slice(index * 4, 4)
            );
    }
}
