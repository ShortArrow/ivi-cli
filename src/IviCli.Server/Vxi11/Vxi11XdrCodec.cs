using System.Buffers.Binary;

namespace IviCli.Server.Vxi11;

/// <summary>
/// Pure XDR primitives (RFC 4506) used by both the portmapper and
/// VXI-11 Core wire formats. Reader/Writer types maintain a cursor over
/// a backing byte span; integers are 4-byte big-endian, strings and
/// opaques are length-prefixed and padded to a 4-byte boundary with
/// zero bytes.
/// </summary>
internal static class Vxi11XdrCodec
{
    /// <summary>
    /// Cursor-based XDR reader. Backed by <see cref="ReadOnlyMemory{T}"/>
    /// rather than a span so instances can flow through async method
    /// boundaries (XDR-decoded parameters are passed into async backend
    /// dispatch calls in <see cref="Vxi11GatewayServer"/>).
    /// </summary>
    public struct XdrReader
    {
        private readonly ReadOnlyMemory<byte> _buffer;
        private int _offset;

        /// <summary>Wraps <paramref name="buffer"/> for sequential XDR reads.</summary>
        public XdrReader(ReadOnlyMemory<byte> buffer)
        {
            _buffer = buffer;
            _offset = 0;
        }

        /// <summary>Current cursor position in bytes.</summary>
        public readonly int Position => _offset;

        /// <summary>Bytes remaining after the cursor.</summary>
        public readonly int Remaining => _buffer.Length - _offset;

        /// <summary>Reads a 4-byte big-endian unsigned integer.</summary>
        public uint ReadUInt32()
        {
            EnsureAvailable(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        /// <summary>Reads a 4-byte big-endian signed integer.</summary>
        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        /// <summary>
        /// Reads an XDR variable-length opaque (4-byte length, body, zero
        /// padding to a 4-byte boundary). Returns the body without padding.
        /// </summary>
        public byte[] ReadOpaque()
        {
            var length = (int)ReadUInt32();
            EnsureAvailable(length);
            var bytes = _buffer.Span.Slice(_offset, length).ToArray();
            _offset += length;
            SkipPadding(length);
            return bytes;
        }

        /// <summary>
        /// Reads an XDR string (same wire shape as opaque) decoded as ASCII.
        /// </summary>
        public string ReadString()
        {
            var bytes = ReadOpaque();
            return System.Text.Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        /// Advances the cursor past a 4-byte-aligned padding region following
        /// a body of <paramref name="bodyLength"/> bytes.
        /// </summary>
        public void SkipPadding(int bodyLength)
        {
            var pad = (4 - (bodyLength & 3)) & 3;
            EnsureAvailable(pad);
            _offset += pad;
        }

        private readonly void EnsureAvailable(int count)
        {
            if (_offset + count > _buffer.Length)
            {
                throw new InvalidDataException(
                    $"XDR read past end of buffer (needed {count}, have {_buffer.Length - _offset})"
                );
            }
        }
    }

    /// <summary>
    /// Growable XDR writer. Backed by a <see cref="List{T}"/> of bytes
    /// so message bodies can be assembled without pre-computing their
    /// total size.
    /// </summary>
    public sealed class XdrWriter
    {
        private readonly List<byte> _buffer = new();

        /// <summary>The bytes written so far.</summary>
        public byte[] ToArray() => _buffer.ToArray();

        /// <summary>Number of bytes written.</summary>
        public int Length => _buffer.Count;

        /// <summary>Writes a 4-byte big-endian unsigned integer.</summary>
        public void WriteUInt32(uint value)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(tmp, value);
            _buffer.Add(tmp[0]);
            _buffer.Add(tmp[1]);
            _buffer.Add(tmp[2]);
            _buffer.Add(tmp[3]);
        }

        /// <summary>Writes a 4-byte big-endian signed integer.</summary>
        public void WriteInt32(int value)
        {
            WriteUInt32(unchecked((uint)value));
        }

        /// <summary>
        /// Writes an XDR variable-length opaque (4-byte length, body,
        /// zero padding to a 4-byte boundary).
        /// </summary>
        public void WriteOpaque(ReadOnlySpan<byte> data)
        {
            WriteUInt32((uint)data.Length);
            for (var i = 0; i < data.Length; i++)
            {
                _buffer.Add(data[i]);
            }
            var pad = (4 - (data.Length & 3)) & 3;
            for (var i = 0; i < pad; i++)
            {
                _buffer.Add(0);
            }
        }

        /// <summary>Writes an XDR string (ASCII-encoded opaque).</summary>
        public void WriteString(string value)
        {
            WriteOpaque(System.Text.Encoding.ASCII.GetBytes(value));
        }

        /// <summary>
        /// Appends raw bytes without an XDR length prefix or padding.
        /// Used to splice an already-encoded procedure body into an RPC
        /// reply envelope.
        /// </summary>
        public void AppendRaw(ReadOnlySpan<byte> bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                _buffer.Add(bytes[i]);
            }
        }
    }

    /// <summary>
    /// Reads one ONC RPC record-marking fragment from <paramref name="stream"/>
    /// and returns its payload (without the 4-byte header). v1 expects
    /// every fragment to carry the LAST_FRAGMENT bit; multi-fragment
    /// records are rejected with <see cref="NotSupportedException"/>.
    /// </summary>
    public static async Task<byte[]> ReadRecordAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, ct);
        var marker = BinaryPrimitives.ReadUInt32BigEndian(header);
        const uint LastFragmentBit = 0x80000000u;
        if ((marker & LastFragmentBit) == 0)
        {
            throw new NotSupportedException(
                "ivi-cli VXI-11 gateway requires single-fragment RPC records"
            );
        }
        var length = (int)(marker & 0x7FFFFFFFu);
        var payload = new byte[length];
        if (length > 0)
        {
            await ReadExactlyAsync(stream, payload, ct);
        }
        return payload;
    }

    /// <summary>
    /// Writes <paramref name="payload"/> to <paramref name="stream"/> as a
    /// single ONC RPC record-marking fragment with the LAST_FRAGMENT bit set.
    /// </summary>
    public static async Task WriteRecordAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x80000000u | (uint)payload.Length);
        await stream.WriteAsync(header, ct);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct);
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"VXI-11 stream closed early at {offset}/{buffer.Length}"
                );
            }
            offset += read;
        }
    }
}
