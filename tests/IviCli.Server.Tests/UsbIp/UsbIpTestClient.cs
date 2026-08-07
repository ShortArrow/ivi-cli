using System.Net;
using System.Net.Sockets;
using IviCli.Domain.Protocols;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>One USBIP_RET_SUBMIT with the data stage that followed it.</summary>
internal sealed record SubmitReply(UsbIpRetSubmit Reply, byte[] Payload);

/// <summary>
/// A usbip client that speaks the wire protocol and nothing else — the
/// same role <c>usbip attach</c> plus <c>vhci-hcd</c> play, with the URB
/// stream driven by the test rather than by a host stack.
/// </summary>
internal sealed class UsbIpTestClient : IDisposable
{
    public const byte DeviceToHostStandardDevice = 0x80;
    public const byte HostToDeviceStandardDevice = 0x00;
    public const byte DeviceToHostClassInterface = 0xA1;
    public const byte HostToDeviceClassInterface = 0x21;

    private readonly TcpClient _tcp;
    private readonly Dictionary<uint, uint> _directions = [];
    private readonly List<byte> _pending = [];
    private NetworkStream? _stream;
    private uint _seqNum;

    public UsbIpTestClient(int port)
    {
        _tcp = new TcpClient();
        _tcp.Connect(IPAddress.Loopback, port);
    }

    private NetworkStream Stream => _stream ??= _tcp.GetStream();

    public async Task<OpRepDevlist> RequestDevlistAsync(CancellationToken ct)
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpReqDevlist(writer, new OpReqDevlist(UsbIpConstants.ProtocolVersion));
        await Stream.WriteAsync(writer.ToArray(), ct);

        // The server closes after answering a devlist, so the whole
        // reply is everything up to end of stream.
        using var buffer = new MemoryStream();
        await Stream.CopyToAsync(buffer, ct);
        var reader = new UsbIpCodec.UsbIpReader(buffer.ToArray());
        return UsbIpCodec.ReadOpRepDevlist(ref reader);
    }

    public async Task<OpRepImport> RequestImportAsync(string busId, CancellationToken ct)
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpReqImport(writer, new OpReqImport(UsbIpConstants.ProtocolVersion, busId));
        await Stream.WriteAsync(writer.ToArray(), ct);

        var preamble = await ReadExactlyAsync(UsbIpConstants.OpHeaderSize, ct);
        var status = (uint)(
            (preamble[4] << 24) | (preamble[5] << 16) | (preamble[6] << 8) | preamble[7]
        );
        var whole =
            status == UsbIpConstants.StatusOk
                ? [.. preamble, .. await ReadExactlyAsync(UsbIpConstants.DeviceInfoSize, ct)]
                : preamble;
        var reader = new UsbIpCodec.UsbIpReader(whole);
        return UsbIpCodec.ReadOpRepImport(ref reader);
    }

    public Task<SubmitReply> ControlInAsync(
        byte bmRequestType,
        byte bRequest,
        ushort wValue,
        ushort wIndex,
        ushort wLength,
        CancellationToken ct
    ) =>
        RoundTripAsync(
            Submit(
                UsbIpConstants.DirIn,
                endpoint: 0,
                new UsbSetupPacket(bmRequestType, bRequest, wValue, wIndex, wLength).ToArray(),
                wLength,
                []
            ),
            ct
        );

    public Task<SubmitReply> ControlOutAsync(
        byte bmRequestType,
        byte bRequest,
        ushort wValue,
        ushort wIndex,
        CancellationToken ct
    ) => ControlOutAsync(bmRequestType, bRequest, wValue, wIndex, [], ct);

    /// <summary>
    /// A host-to-device control transfer whose data stage carries
    /// <paramref name="data"/> — SET_LINE_CODING is the first request in
    /// this server whose meaning lives there rather than in the setup
    /// packet.
    /// </summary>
    public Task<SubmitReply> ControlOutAsync(
        byte bmRequestType,
        byte bRequest,
        ushort wValue,
        ushort wIndex,
        byte[] data,
        CancellationToken ct
    ) =>
        RoundTripAsync(
            Submit(
                UsbIpConstants.DirOut,
                endpoint: 0,
                new UsbSetupPacket(
                    bmRequestType,
                    bRequest,
                    wValue,
                    wIndex,
                    (ushort)data.Length
                ).ToArray(),
                data.Length,
                data
            ),
            ct
        );

    public Task<SubmitReply> BulkOutAsync(byte[] transfer, CancellationToken ct) =>
        RoundTripAsync(SubmitBulkOut(transfer), ct);

    public Task<SubmitReply> BulkInAsync(int bufferLength, CancellationToken ct) =>
        RoundTripAsync(SubmitBulkIn(bufferLength), ct);

    public Task<SubmitReply> InterruptInAsync(int bufferLength, CancellationToken ct) =>
        RoundTripAsync(SubmitInterruptIn(bufferLength), ct);

    public uint SubmitBulkOut(byte[] transfer) =>
        Submit(UsbIpConstants.DirOut, endpoint: 1, NoSetup, transfer.Length, transfer);

    public uint SubmitBulkIn(int bufferLength) =>
        Submit(UsbIpConstants.DirIn, endpoint: 1, NoSetup, bufferLength, []);

    public uint SubmitInterruptIn(int bufferLength) =>
        Submit(UsbIpConstants.DirIn, endpoint: 2, NoSetup, bufferLength, []);

    public uint SubmitUnlink(uint targetSeqNum)
    {
        var seqNum = ++_seqNum;
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteCmdUnlink(
            writer,
            new UsbIpCmdUnlink(
                new UsbIpHeaderBasic(UsbIpConstants.CmdUnlink, seqNum, DevId, 0, 0),
                targetSeqNum
            )
        );
        _pending.AddRange(writer.ToArray());
        return seqNum;
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        var bytes = _pending.ToArray();
        _pending.Clear();
        await Stream.WriteAsync(bytes, ct);
    }

    public async Task<SubmitReply> ReadSubmitReplyAsync(CancellationToken ct)
    {
        var header = await ReadExactlyAsync(UsbIpConstants.CommandHeaderSize, ct);
        var reader = new UsbIpCodec.UsbIpReader(header);
        var reply = UsbIpCodec.ReadRetSubmit(ref reader);
        var length = UsbIpCodec.RetSubmitPayloadLength(_directions[reply.Header.SeqNum], reply);
        var payload = length > 0 ? await ReadExactlyAsync(length, ct) : [];
        return new SubmitReply(reply, payload);
    }

    public async Task<UsbIpRetUnlink> ReadUnlinkReplyAsync(CancellationToken ct)
    {
        var header = await ReadExactlyAsync(UsbIpConstants.CommandHeaderSize, ct);
        var reader = new UsbIpCodec.UsbIpReader(header);
        return UsbIpCodec.ReadRetUnlink(ref reader);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _tcp.Dispose();
    }

    private async Task<SubmitReply> RoundTripAsync(uint seqNum, CancellationToken ct)
    {
        await FlushAsync(ct);
        var reply = await ReadSubmitReplyAsync(ct);
        reply.Reply.Header.SeqNum.ShouldBe(seqNum);
        return reply;
    }

    private uint Submit(
        uint direction,
        uint endpoint,
        byte[] setup,
        int transferBufferLength,
        byte[] outPayload
    )
    {
        var seqNum = ++_seqNum;
        _directions[seqNum] = direction;
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteCmdSubmit(
            writer,
            new UsbIpCmdSubmit(
                Header: new UsbIpHeaderBasic(
                    UsbIpConstants.CmdSubmit,
                    seqNum,
                    DevId,
                    direction,
                    endpoint
                ),
                TransferFlags: 0,
                TransferBufferLength: transferBufferLength,
                StartFrame: 0,
                NumberOfPackets: UsbIpConstants.NumberOfPacketsNonIso,
                Interval: 0,
                Setup: setup
            )
        );
        _pending.AddRange(writer.ToArray());
        _pending.AddRange(outPayload);
        return seqNum;
    }

    private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await Stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
            {
                throw new EndOfStreamException($"gateway closed after {offset} of {count} bytes");
            }
            offset += read;
        }
        return buffer;
    }

    private static readonly byte[] NoSetup = new byte[UsbIpConstants.SetupSize];

    private const uint DevId = 0x0001_0001;
}
