namespace IviCli.Domain.Servers;

/// <summary>
/// Which USB profile an exported device presents to the host that
/// attaches it (ADR 0049 §5).
///
/// It belongs to the export rather than to the USB protocol layer: the
/// same mock device answers the same SCPI either way, and what the choice
/// decides is which descriptors the host reads and therefore which inbox
/// driver binds — a USBTMC instrument or a COM port.
/// </summary>
public enum UsbExportProfile
{
    /// <summary>
    /// USBTMC-USB488 (ADR 0049 §2): the instrument-shaped profile, bound
    /// by the inbox USBTMC class driver and seen by a VISA as a
    /// <c>USB::…::INSTR</c> resource. The default, because an emulated
    /// instrument is what the device server exists to export.
    /// </summary>
    UsbTmc = 0,

    /// <summary>
    /// CDC-ACM (ADR 0049 §5): the serial-shaped profile, bound by the
    /// inbox <c>usbser.sys</c> on Windows and <c>cdc-acm</c> on Linux, so
    /// a real COM port (or <c>/dev/ttyACM*</c>) appears for tools that
    /// speak nothing else.
    /// </summary>
    CdcAcm = 1,
}
