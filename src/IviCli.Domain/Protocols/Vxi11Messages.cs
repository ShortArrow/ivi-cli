namespace IviCli.Domain.Protocols;

/// <summary>
/// Parsed RPC call header (RFC 1831 §9). Cred / verf are restricted to
/// <c>AUTH_NONE</c> in this gateway, so they do not surface here; an
/// attempt to use any other auth flavor is rejected during decode.
/// </summary>
public readonly record struct RpcCallHeader(uint Xid, uint Program, uint Version, uint Procedure);

/// <summary>Decoded Create_LinkParms structure (VXI-11 §6 / create_link).</summary>
public readonly record struct CreateLinkParms(
    int ClientId,
    bool LockDevice,
    uint LockTimeout,
    string Device
);

/// <summary>Decoded Device_WriteParms structure (VXI-11 §6 / device_write).</summary>
public readonly record struct DeviceWriteParms(
    int Lid,
    uint IoTimeout,
    uint LockTimeout,
    int Flags,
    byte[] Data
);

/// <summary>Decoded Device_ReadParms structure (VXI-11 §6 / device_read).</summary>
public readonly record struct DeviceReadParms(
    int Lid,
    uint RequestSize,
    uint IoTimeout,
    uint LockTimeout,
    int Flags,
    byte TermChar
);

/// <summary>Decoded Device_GenericParms structure (used by device_clear).</summary>
public readonly record struct DeviceGenericParms(
    int Lid,
    int Flags,
    uint LockTimeout,
    uint IoTimeout
);

/// <summary>
/// Decoded <c>Device_RemoteFunc</c> structure (VXI-11 §B.6.32 /
/// device_create_intr_chan).
/// </summary>
public readonly record struct DeviceRemoteFunc(
    uint HostAddr,
    uint HostPort,
    uint ProgNum,
    uint ProgVers,
    int ProgFamily
);

/// <summary>
/// Decoded <c>Device_EnableSrqParms</c> structure (VXI-11 §B.6.31 /
/// device_enable_srq). The handle is up to 40 bytes the client picks;
/// the server echoes it back on every SRQ delivery so the client can
/// correlate which link raised the request.
/// </summary>
public readonly record struct DeviceEnableSrqParms(int Lid, bool Enable, byte[] Handle);

/// <summary>
/// Decoded <c>Device_SrqParms</c> structure (VXI-11 §B.6.40 /
/// device_intr_srq, server → client). Carries the same handle the
/// client passed into <see cref="DeviceEnableSrqParms"/>.
/// </summary>
public readonly record struct DeviceSrqParms(byte[] Handle);
