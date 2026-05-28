namespace IviCli.Domain.Protocols;

/// <summary>
/// XDR encode/decode helpers for the VXI-11 Interrupt-channel RPC
/// payloads added by ADR 0042 (<see cref="DeviceRemoteFunc"/>,
/// <see cref="DeviceEnableSrqParms"/>, <see cref="DeviceSrqParms"/>).
/// Kept apart from the dispatch logic so the same encode/decode pair
/// is shared between the gateway server (decoding client calls,
/// encoding the outgoing device_intr_srq) and the client backend
/// (mirror role).
/// </summary>
public static class Vxi11InterruptCodec
{
    /// <summary>Decodes <see cref="DeviceRemoteFunc"/> from the supplied reader.</summary>
    public static DeviceRemoteFunc ReadRemoteFunc(ref Vxi11XdrCodec.XdrReader reader) =>
        new(
            HostAddr: reader.ReadUInt32(),
            HostPort: reader.ReadUInt32(),
            ProgNum: reader.ReadUInt32(),
            ProgVers: reader.ReadUInt32(),
            ProgFamily: reader.ReadInt32()
        );

    /// <summary>Writes <see cref="DeviceRemoteFunc"/> via the supplied writer.</summary>
    public static void WriteRemoteFunc(Vxi11XdrCodec.XdrWriter writer, DeviceRemoteFunc parms)
    {
        writer.WriteUInt32(parms.HostAddr);
        writer.WriteUInt32(parms.HostPort);
        writer.WriteUInt32(parms.ProgNum);
        writer.WriteUInt32(parms.ProgVers);
        writer.WriteInt32(parms.ProgFamily);
    }

    /// <summary>Decodes <see cref="DeviceEnableSrqParms"/>.</summary>
    public static DeviceEnableSrqParms ReadEnableSrqParms(ref Vxi11XdrCodec.XdrReader reader) =>
        new(Lid: reader.ReadInt32(), Enable: reader.ReadUInt32() != 0, Handle: reader.ReadOpaque());

    /// <summary>Writes <see cref="DeviceEnableSrqParms"/>.</summary>
    public static void WriteEnableSrqParms(
        Vxi11XdrCodec.XdrWriter writer,
        DeviceEnableSrqParms parms
    )
    {
        writer.WriteInt32(parms.Lid);
        writer.WriteUInt32(parms.Enable ? 1u : 0u);
        writer.WriteOpaque(parms.Handle);
    }

    /// <summary>Decodes <see cref="DeviceSrqParms"/>.</summary>
    public static DeviceSrqParms ReadSrqParms(ref Vxi11XdrCodec.XdrReader reader) =>
        new(Handle: reader.ReadOpaque());

    /// <summary>Writes <see cref="DeviceSrqParms"/>.</summary>
    public static void WriteSrqParms(Vxi11XdrCodec.XdrWriter writer, DeviceSrqParms parms)
    {
        writer.WriteOpaque(parms.Handle);
    }
}
