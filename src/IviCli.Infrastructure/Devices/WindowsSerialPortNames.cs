using Microsoft.Win32;

namespace IviCli.Infrastructure.Devices;

/// <summary>
/// The COM ports Windows lists under <c>HKLM\HARDWARE\DEVICEMAP\SERIALCOMM</c>,
/// the same source <c>SerialPort.GetPortNames</c> reads.
/// </summary>
internal static class WindowsSerialPortNames
{
    private const string SerialCommKey = @"HARDWARE\DEVICEMAP\SERIALCOMM";

    /// <summary>
    /// The listed port names; none on another operating system or when the
    /// key is absent, as it is on a machine that has never had a serial port.
    /// </summary>
    public static IReadOnlyCollection<string> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        using var key = Registry.LocalMachine.OpenSubKey(SerialCommKey);
        if (key is null)
        {
            return [];
        }
        var names = new List<string>();
        foreach (var valueName in key.GetValueNames())
        {
            if (key.GetValue(valueName) is string portName)
            {
                names.Add(portName);
            }
        }
        return names;
    }
}
