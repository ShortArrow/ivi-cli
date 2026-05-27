using System.Buffers.Binary;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// Client-side ONC RPC record-marking framing (RFC 1831 §10) over a
/// <see cref="System.IO.Stream"/>. Duplicates the Server-side framing
/// helper rather than depending on it, so the client backend stays
/// free of any reference to the Server assembly (ADR 0021 §3).
/// </summary>
public static class Vxi11RecordFraming
{
    /// <summary>
    /// Reads one record fragment from <paramref name="stream"/> and
    /// returns its payload (without the 4-byte header). v1 expects every
    /// fragment to carry the LAST_FRAGMENT bit; multi-fragment records
    /// are rejected with <see cref="NotSupportedException"/>.
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
                "ivi-cli VXI-11 client requires single-fragment RPC records"
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
    /// single fragment with the LAST_FRAGMENT bit set.
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
