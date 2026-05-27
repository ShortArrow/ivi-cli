using System.Buffers.Binary;

namespace IviCli.Domain.Protocols;

/// <summary>
/// Pure XDR primitives (RFC 4506) shared by the VXI-11 gateway server
/// and the VXI-11 client backend. Reader/Writer types maintain a cursor
/// over a backing byte buffer; integers are 4-byte big-endian, strings
/// and opaques are length-prefixed and padded to a 4-byte boundary with
/// zero bytes. Stream-level record-marking framing lives outside this
/// type so the Domain layer stays free of <see cref="System.IO.Stream"/>
/// dependencies (mirrors the boundary used by <see cref="HiSlipMessage"/>).
/// </summary>
public static class Vxi11XdrCodec
{
    /// <summary>
    /// Cursor-based XDR reader. Backed by <see cref="ReadOnlyMemory{T}"/>
    /// rather than a span so instances can flow through async method
    /// boundaries — VXI-11 dispatch reads procedure arguments before
    /// awaiting an async backend call.
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
    /// Growable XDR writer. Backed by a <see cref="List{T}"/> of bytes so
    /// message bodies can be assembled without pre-computing total size.
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
        /// Writes an XDR variable-length opaque (4-byte length, body, zero
        /// padding to a 4-byte boundary).
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
        /// Appends raw bytes without an XDR length prefix or padding. Used
        /// to splice an already-encoded procedure body into an RPC reply
        /// envelope.
        /// </summary>
        public void AppendRaw(ReadOnlySpan<byte> bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                _buffer.Add(bytes[i]);
            }
        }
    }
}
