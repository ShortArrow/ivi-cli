namespace IviCli.Domain.Protocols;

/// <summary>
/// The CDC data-stream exchange with no transport under it: bulk-OUT
/// transfers in, the lines they closed and bulk-IN transfers out, and the
/// only state between them — the bytes of a line that no terminator has
/// closed yet, and the answer bytes waiting for an URB to carry them.
///
/// A CDC data pipe has no framing (CDC 1.1 §3.6.2 hands the class a byte
/// stream and nothing else), so the structure has to come from the
/// content. It comes from the same rule the SOCKET gateway frames SCPI
/// with: a line ends at a newline, a carriage return before it belongs to
/// the terminator, and the terminator is not part of the line. What an
/// empty line means, and what any line means at all, belongs to the
/// caller — the SOCKET gateway drops empty lines before dispatch.
///
/// Deliberately free of SCPI, of asynchrony and of I/O, exactly as
/// <see cref="UsbTmcMessagePump"/> is. Phase 5b's server loop turns URBs
/// into these calls and the returned bytes back into URBs.
/// </summary>
public sealed class CdcAcmStreamPump
{
    private readonly List<byte> _accumulated = [];
    private readonly List<byte> _response = [];
    private int _responseOffset;

    /// <summary>True while bytes have arrived that no terminator closed.</summary>
    public bool IsAccumulating => _accumulated.Count > 0;

    /// <summary>True while answer bytes are waiting for a bulk-IN transfer.</summary>
    public bool HasPendingResponse => _responseOffset < _response.Count;

    /// <summary>
    /// Feeds one bulk-OUT transfer through the exchange and returns the
    /// lines it closed, in arrival order — none when the transfer ended
    /// mid-line, several when it carried several terminators. The bytes
    /// are the ones that arrived, decoded by nobody.
    /// </summary>
    public IReadOnlyList<byte[]> SubmitBulkOut(ReadOnlySpan<byte> transfer)
    {
        var lines = new List<byte[]>();
        for (var i = 0; i < transfer.Length; i++)
        {
            if (transfer[i] != LineFeed)
            {
                _accumulated.Add(transfer[i]);
                continue;
            }

            lines.Add(TakeAccumulatedLine());
        }

        return lines;
    }

    /// <summary>
    /// Hands the pump the bytes that answer the host — in Phase 5b,
    /// whatever the backend produced, line ending included. Bytes queue
    /// behind whatever is still waiting, because the stream is one
    /// sequence rather than a series of messages.
    /// </summary>
    public void SupplyResponse(ReadOnlySpan<byte> response)
    {
        for (var i = 0; i < response.Length; i++)
        {
            _response.Add(response[i]);
        }
    }

    /// <summary>
    /// Takes up to <paramref name="maxLength"/> of the queued answer
    /// bytes. Returns false when none are queued: a bulk-IN URB completes
    /// with whatever exists, and a stream has no end-of-message to hold
    /// one back for.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxLength"/> is negative, which no URB can be.
    /// </exception>
    public bool TryTakeBulkIn(int maxLength, out byte[] chunk)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);

        chunk = [];
        var remaining = _response.Count - _responseOffset;
        var take = Math.Min(remaining, maxLength);
        if (take <= 0)
        {
            return false;
        }

        chunk = _response.GetRange(_responseOffset, take).ToArray();
        _responseOffset += take;
        if (!HasPendingResponse)
        {
            DiscardResponse();
        }

        return true;
    }

    /// <summary>
    /// Returns the exchange to its just-enumerated state: the half-line
    /// dropped and the queued answer with it, so a host that reopens the
    /// port does not read the previous session's tail.
    /// </summary>
    public void Clear()
    {
        _accumulated.Clear();
        DiscardResponse();
    }

    /// <summary>
    /// Closes the accumulated line at a terminator: a carriage return
    /// immediately before the newline is part of that terminator, which
    /// is what makes a CRLF terminal and a bare-newline client produce
    /// the same line.
    /// </summary>
    private byte[] TakeAccumulatedLine()
    {
        var length = _accumulated.Count;
        while (length > 0 && _accumulated[length - 1] == CarriageReturn)
        {
            length--;
        }

        var line = _accumulated.GetRange(0, length).ToArray();
        _accumulated.Clear();
        return line;
    }

    private void DiscardResponse()
    {
        _response.Clear();
        _responseOffset = 0;
    }

    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';
}
