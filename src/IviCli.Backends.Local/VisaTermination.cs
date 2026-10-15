using System.Text;
using IviCli.Domain.Devices;

namespace IviCli.Backends.Local;

/// <summary>
/// A device's terminators over VISA (ADR 0055 §5). VISA ends a read at one
/// character, the last of the read terminator, so with CRLF a reply that
/// ends in a bare LF also ends the read. Text passes as Latin-1, so every
/// byte passes unchanged, as on the Serial backend.
/// </summary>
public static class VisaTermination
{
    /// <summary>The character VISA ends a read at: the last of <paramref name="terminator"/>.</summary>
    public static byte Character(Termination terminator)
    {
        ArgumentNullException.ThrowIfNull(terminator);
        return (byte)terminator.Value[^1];
    }

    /// <summary>
    /// Reads one reply from <paramref name="readChunk"/>, which returns what
    /// one VISA read returned, until a read ends with the termination
    /// character, and returns the reply without it, and without a CR right
    /// before it when <paramref name="terminator"/> is CRLF.
    /// </summary>
    /// <exception cref="IOException">A read returned nothing.</exception>
    public static string ReadMessage(Func<byte[]> readChunk, Termination terminator)
    {
        ArgumentNullException.ThrowIfNull(readChunk);
        var end = Character(terminator);
        var reply = new List<byte>();
        while (reply.Count == 0 || reply[^1] != end)
        {
            var chunk = readChunk();
            if (chunk.Length == 0)
            {
                throw new IOException("a read returned nothing before the termination character");
            }
            reply.AddRange(chunk);
        }
        var length = reply.Count - 1;
        if (terminator == Termination.CrLf && length > 0 && reply[length - 1] == (byte)'\r')
        {
            length--;
        }
        return Encoding.Latin1.GetString(reply.ToArray(), 0, length);
    }

    /// <summary>The bytes of <paramref name="text"/> followed by <paramref name="terminator"/>.</summary>
    public static byte[] Encode(string text, Termination terminator)
    {
        ArgumentNullException.ThrowIfNull(terminator);
        return Encoding.Latin1.GetBytes(text + terminator.Value);
    }
}
