using System.Buffers.Binary;

namespace IviCli.Domain.Protocols;

/// <summary>
/// Pure HiSLIP message framing per IVI-6.1 §10. Headers are 16 bytes:
/// 'S' prologue, type, control code, 4-byte message parameter (BE), 8-byte
/// payload length (BE), then payload (variable, length given by header).
/// </summary>
public static class HiSlipMessage
{
    /// <summary>HiSLIP header size in bytes (including the prologue).</summary>
    public const int HeaderSize = 16;

    /// <summary>The HiSLIP prologue byte ('S').</summary>
    public const byte Prologue = 0x53;

    /// <summary>Encodes a HiSLIP header into <paramref name="destination"/>.</summary>
    public static void WriteHeader(
        Span<byte> destination,
        HiSlipMessageType type,
        byte controlCode,
        uint messageParameter,
        ulong payloadLength
    )
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"destination must be at least {HeaderSize} bytes",
                nameof(destination)
            );
        }
        destination[0] = Prologue;
        destination[1] = (byte)type;
        destination[2] = controlCode;
        BinaryPrimitives.WriteUInt32BigEndian(destination[3..7], messageParameter);
        BinaryPrimitives.WriteUInt64BigEndian(destination[7..15], payloadLength);
        destination[15] = 0;
    }

    /// <summary>Decodes a HiSLIP header from <paramref name="source"/>.</summary>
    public static HiSlipHeader ReadHeader(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"source must contain at least {HeaderSize} bytes",
                nameof(source)
            );
        }
        if (source[0] != Prologue)
        {
            throw new InvalidDataException(
                $"HiSLIP prologue mismatch: expected 0x{Prologue:X2}, got 0x{source[0]:X2}"
            );
        }
        var type = (HiSlipMessageType)source[1];
        var controlCode = source[2];
        var messageParameter = BinaryPrimitives.ReadUInt32BigEndian(source[3..7]);
        var payloadLength = BinaryPrimitives.ReadUInt64BigEndian(source[7..15]);
        return new HiSlipHeader(type, controlCode, messageParameter, payloadLength);
    }
}

/// <summary>A decoded HiSLIP message header.</summary>
public readonly record struct HiSlipHeader(
    HiSlipMessageType Type,
    byte ControlCode,
    uint MessageParameter,
    ulong PayloadLength
);

/// <summary>HiSLIP message type codes per IVI-6.1 §10.</summary>
public enum HiSlipMessageType : byte
{
    /// <summary>Initialize request from client (handshake start).</summary>
    Initialize = 0,

    /// <summary>Initialize response from server.</summary>
    InitializeResponse = 1,

    /// <summary>Fatal error message; terminates the connection.</summary>
    FatalError = 2,

    /// <summary>Non-fatal error.</summary>
    Error = 3,

    /// <summary>Synchronous data with intermediate flag.</summary>
    Data = 4,

    /// <summary>Synchronous data with end-of-message flag.</summary>
    DataEnd = 5,

    /// <summary>Device-clear complete.</summary>
    DeviceClearComplete = 6,

    /// <summary>Async device-clear acknowledgement.</summary>
    DeviceClearAcknowledge = 7,

    /// <summary>Async initialize request (control channel handshake).</summary>
    AsyncInitialize = 16,

    /// <summary>Async initialize response (server -> client).</summary>
    AsyncInitializeResponse = 17,

    /// <summary>Maximum message-size advertisement.</summary>
    AsyncMaximumMessageSize = 27,

    /// <summary>Maximum message-size response.</summary>
    AsyncMaximumMessageSizeResponse = 28,

    // ----- HiSLIP v2 (ADR 0007 §1.5) -----

    /// <summary>Async device-clear request (client -> server on async channel).</summary>
    AsyncDeviceClear = 12,

    /// <summary>Async device-clear acknowledge (server -> client on async channel).</summary>
    AsyncDeviceClearAcknowledge = 13,

    /// <summary>Async lock request (client -> server). Control byte 1 = acquire.</summary>
    AsyncLock = 18,

    /// <summary>Async lock response. Control byte 1 = granted, 0 = denied.</summary>
    AsyncLockResponse = 19,

    /// <summary>Async release lock (client -> server).</summary>
    AsyncReleaseLock = 29,

    /// <summary>Server-pushed service request notification (server -> client).</summary>
    ServiceRequest = 30,
}
