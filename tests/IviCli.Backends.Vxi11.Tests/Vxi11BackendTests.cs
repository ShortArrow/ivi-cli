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
    public async Task CloseAsync_is_noop_when_session_was_never_opened()
    {
        var backend = new Vxi11Backend(12345);
        var device = BuildDevice();

        var result = await backend.CloseAsync(device, default);

        result.ShouldBeOk();
    }

    private static Device BuildDevice() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
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
            return new StubCall(xid, proc);
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

    private readonly record struct StubCall(uint Xid, uint Procedure);
}
