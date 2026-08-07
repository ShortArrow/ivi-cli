using System.Buffers.Binary;

namespace IviCli.Domain.Protocols;

/// <summary>
/// The line coding of PSTN 1.1 §6.3.11: the seven bytes SET_LINE_CODING
/// carries in and GET_LINE_CODING carries out, little endian like every
/// other USB field.
///
/// The emulated device has no UART behind it, so none of these fields
/// changes how a single byte travels. They are state the host writes and
/// reads back, which is all a serial terminal's port settings need to
/// behave the way an operator expects.
/// </summary>
/// <param name="DteRate"><c>dwDTERate</c>, bits per second.</param>
/// <param name="CharFormat">
/// <c>bCharFormat</c>: 0 for one stop bit, 1 for 1.5, 2 for two.
/// </param>
/// <param name="ParityType">
/// <c>bParityType</c>: 0 none, 1 odd, 2 even, 3 mark, 4 space.
/// </param>
/// <param name="DataBits"><c>bDataBits</c>: 5, 6, 7, 8 or 16.</param>
public readonly record struct CdcLineCoding(
    uint DteRate,
    byte CharFormat,
    byte ParityType,
    byte DataBits
)
{
    /// <summary>Size of the structure on the wire.</summary>
    public const int Size = 7;

    /// <summary>
    /// The coding the device reports before the host has set one. 115200
    /// 8-N-1 is what a serial terminal opens with unless told otherwise,
    /// so a host that never sets a coding still reads a coherent one.
    /// </summary>
    public static CdcLineCoding Default =>
        new(DteRate: DefaultDteRate, CharFormat: OneStopBit, ParityType: NoParity, DataBits: 8);

    /// <summary>Decodes the seven bytes of a SET_LINE_CODING data stage.</summary>
    /// <exception cref="InvalidDataException">
    /// <paramref name="coding"/> is not exactly <see cref="Size"/> bytes,
    /// so no field can be located reliably.
    /// </exception>
    public static CdcLineCoding Read(ReadOnlySpan<byte> coding)
    {
        if (coding.Length != Size)
        {
            throw new InvalidDataException(
                $"Line coding must be exactly {Size} bytes, was {coding.Length}"
            );
        }

        return new CdcLineCoding(
            DteRate: BinaryPrimitives.ReadUInt32LittleEndian(coding[..DteRateLength]),
            CharFormat: coding[4],
            ParityType: coding[5],
            DataBits: coding[6]
        );
    }

    /// <summary>Encodes the structure into a fresh <see cref="Size"/>-byte array.</summary>
    public byte[] ToArray()
    {
        var bytes = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, DteRateLength), DteRate);
        bytes[4] = CharFormat;
        bytes[5] = ParityType;
        bytes[6] = DataBits;
        return bytes;
    }

    private const int DteRateLength = 4;
    private const uint DefaultDteRate = 115200;
    private const byte OneStopBit = 0;
    private const byte NoParity = 0;
}
