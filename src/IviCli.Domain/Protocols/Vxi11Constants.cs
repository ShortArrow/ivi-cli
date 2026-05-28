namespace IviCli.Domain.Protocols;

/// <summary>
/// Wire-level constants for the VXI-11 Core channel
/// (program 395183 / version 1) and the co-located portmapper
/// (program 100000 / version 2). Public so both the gateway server
/// and the client backend can compose calls from the same source of
/// truth — see ADR 0029.
/// </summary>
public static class Vxi11Constants
{
    /// <summary>VXI-11 Core program number.</summary>
    public const uint CoreProgram = 395183;

    /// <summary>VXI-11 Core program version.</summary>
    public const uint CoreVersion = 1;

    /// <summary>VXI-11 Abort program number.</summary>
    public const uint AbortProgram = 395184;

    /// <summary>VXI-11 Abort program version.</summary>
    public const uint AbortVersion = 1;

    /// <summary>Abort: device_abort (proc 1). Sole procedure on the abort channel.</summary>
    public const uint ProcDeviceAbort = 1;

    /// <summary>VXI-11 Interrupt program (server → client SRQ delivery, ADR 0042).</summary>
    public const uint InterruptProgram = 395185;

    /// <summary>VXI-11 Interrupt program version.</summary>
    public const uint InterruptVersion = 1;

    /// <summary>Interrupt: device_intr_srq (proc 30). Sole procedure, server → client.</summary>
    public const uint ProcDeviceIntrSrq = 30;

    /// <summary>Core: device_enable_srq (proc 18).</summary>
    public const uint ProcDeviceEnableSrq = 18;

    /// <summary>Core: device_create_intr_chan (proc 25).</summary>
    public const uint ProcCreateIntrChan = 25;

    /// <summary>Core: device_destroy_intr_chan (proc 26).</summary>
    public const uint ProcDestroyIntrChan = 26;

    /// <summary>ONC RPC progFamily for TCP transport (per RFC 5531).</summary>
    public const int ProgFamilyTcp = 6;

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

    /// <summary>Core: device_trigger (proc 17, ADR 0041).</summary>
    public const uint ProcDeviceTrigger = 17;

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

    /// <summary>device_read reason flag: END (entire message delivered).</summary>
    public const int ReadReasonEnd = 4;
}
