namespace IviCli.Server.Vxi11;

/// <summary>
/// Wire-level constants and request/response record shapes for the
/// VXI-11 Core channel (program 395183 / version 1) and the co-located
/// portmapper companion (program 100000 / version 2). Wire details
/// live inside the Server layer because they are protocol-internal
/// and never cross a layer boundary (ADR 0021 §3).
/// </summary>
internal static class Vxi11Constants
{
    /// <summary>VXI-11 Core program number.</summary>
    public const uint CoreProgram = 395183;

    /// <summary>VXI-11 Core program version.</summary>
    public const uint CoreVersion = 1;

    /// <summary>ONC portmapper program number.</summary>
    public const uint PortmapProgram = 100000;

    /// <summary>ONC portmapper version.</summary>
    public const uint PortmapVersion = 2;

    /// <summary>Portmapper GETPORT procedure.</summary>
    public const uint PortmapGetPort = 3;

    /// <summary>Core: create_link.</summary>
    public const uint ProcCreateLink = 10;

    /// <summary>Core: device_write.</summary>
    public const uint ProcDeviceWrite = 11;

    /// <summary>Core: device_read.</summary>
    public const uint ProcDeviceRead = 12;

    /// <summary>Core: device_clear.</summary>
    public const uint ProcDeviceClear = 14;

    /// <summary>Core: destroy_link.</summary>
    public const uint ProcDestroyLink = 23;

    /// <summary>RPC reply status: MSG_ACCEPTED.</summary>
    public const uint MsgAccepted = 0;

    /// <summary>RPC accept status: SUCCESS.</summary>
    public const uint AcceptSuccess = 0;

    /// <summary>RPC accept status: PROG_UNAVAIL.</summary>
    public const uint AcceptProgUnavail = 1;

    /// <summary>RPC accept status: PROG_MISMATCH.</summary>
    public const uint AcceptProgMismatch = 2;

    /// <summary>RPC accept status: PROC_UNAVAIL.</summary>
    public const uint AcceptProcUnavail = 3;

    /// <summary>VXI-11 error code: no error.</summary>
    public const int Vxi11NoError = 0;

    /// <summary>VXI-11 error code: syntax error.</summary>
    public const int Vxi11SyntaxError = 1;

    /// <summary>VXI-11 error code: invalid link identifier.</summary>
    public const int Vxi11InvalidLink = 4;

    /// <summary>VXI-11 error code: operation not supported.</summary>
    public const int Vxi11NotSupported = 8;

    /// <summary>VXI-11 error code: I/O timeout.</summary>
    public const int Vxi11IoTimeout = 15;

    /// <summary>VXI-11 error code: I/O error.</summary>
    public const int Vxi11IoError = 17;

    /// <summary>device_write flag bit signalling end of message.</summary>
    public const int WriteEndFlag = 0x08;
}

/// <summary>
/// Parsed RPC call header (RFC 1831 §9). Cred / verf are restricted to
/// <c>AUTH_NONE</c> in this gateway, so we do not surface them; an
/// attempt to use any other auth flavor is rejected during decode.
/// </summary>
internal readonly record struct RpcCallHeader(uint Xid, uint Program, uint Version, uint Procedure);

/// <summary>Result of decoding a Create_LinkParms structure.</summary>
internal readonly record struct CreateLinkParms(
    int ClientId,
    bool LockDevice,
    uint LockTimeout,
    string Device
);

/// <summary>Result of decoding a Device_WriteParms structure.</summary>
internal readonly record struct DeviceWriteParms(
    int Lid,
    uint IoTimeout,
    uint LockTimeout,
    int Flags,
    byte[] Data
);

/// <summary>Result of decoding a Device_ReadParms structure.</summary>
internal readonly record struct DeviceReadParms(
    int Lid,
    uint RequestSize,
    uint IoTimeout,
    uint LockTimeout,
    int Flags,
    byte TermChar
);

/// <summary>Result of decoding a Device_GenericParms structure (used by clear).</summary>
internal readonly record struct DeviceGenericParms(
    int Lid,
    int Flags,
    uint LockTimeout,
    uint IoTimeout
);
