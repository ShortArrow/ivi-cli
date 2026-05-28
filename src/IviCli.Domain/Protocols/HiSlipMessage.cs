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
    // ----- HiSLIP v1 / v2 / v3 (IVI-6.1 §10 message-type table) -----
    // Values below match the IVI-6.1 specification. v3 codes not yet
    // honoured by this server (10-14, 21-22, 25-26 — remote/local,
    // status query, lock-info, etc.) remain intentionally absent so
    // a stray enum cast to one of those bytes cannot silently succeed.

    /// <summary>Initialize request from client (handshake start). Spec value 0.</summary>
    Initialize = 0,

    /// <summary>Initialize response from server. Spec value 1.</summary>
    InitializeResponse = 1,

    /// <summary>Fatal error message; terminates the connection. Spec value 2.</summary>
    FatalError = 2,

    /// <summary>Non-fatal error. Spec value 3.</summary>
    Error = 3,

    /// <summary>Async lock request (client -> server). Control byte 1 = acquire, 0 = release. Spec value 4.</summary>
    AsyncLock = 4,

    /// <summary>Async lock response. Control byte 1 = granted, 0 = denied. Spec value 5.</summary>
    AsyncLockResponse = 5,

    /// <summary>Synchronous data with intermediate flag. Spec value 6.</summary>
    Data = 6,

    /// <summary>Synchronous data with end-of-message flag. Spec value 7.</summary>
    DataEnd = 7,

    /// <summary>Device-clear complete on the sync channel. Spec value 8.</summary>
    DeviceClearComplete = 8,

    /// <summary>Sync-channel device-clear acknowledgement. Spec value 9.</summary>
    DeviceClearAcknowledge = 9,

    /// <summary>Maximum message-size advertisement on async channel. Spec value 15.</summary>
    AsyncMaximumMessageSize = 15,

    /// <summary>Maximum message-size response on async channel. Spec value 16.</summary>
    AsyncMaximumMessageSizeResponse = 16,

    /// <summary>Async initialize request (control channel handshake). Spec value 17.</summary>
    AsyncInitialize = 17,

    /// <summary>Async initialize response (server -> client). Spec value 18.</summary>
    AsyncInitializeResponse = 18,

    /// <summary>Async device-clear request (client -> server on async channel). Spec value 19.</summary>
    AsyncDeviceClear = 19,

    /// <summary>Server-pushed service request notification (server -> client). Spec value 20.</summary>
    ServiceRequest = 20,

    /// <summary>Async-channel device-clear acknowledgement (server -> client). Spec value 23.</summary>
    AsyncDeviceClearAcknowledge = 23,

    /// <summary>Trigger (IVI-6.1 §10.4 — v3). Sync-channel only. Spec value 24.</summary>
    Trigger = 24,
}
