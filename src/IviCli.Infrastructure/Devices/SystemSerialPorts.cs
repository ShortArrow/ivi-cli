using System.IO.Abstractions;
using IviCli.Application.Devices;

namespace IviCli.Infrastructure.Devices;

/// <summary>
/// <see cref="ISerialPorts"/> over the machine's serial ports. On Linux and
/// macOS a port is a device file, and two names are one port when they
/// resolve to the same file. On Windows a port is a COM name, compared in
/// one letter case and without the <c>\\.\</c> prefix, and it is present
/// while the system lists it.
/// </summary>
public sealed class SystemSerialPorts : ISerialPorts
{
    private const string WindowsDevicePrefix = @"\\.\";

    private readonly IFileSystem _fs;
    private readonly bool _windows;
    private readonly Func<IReadOnlyCollection<string>> _windowsPortNames;

    /// <summary>Uses the real file system and, on Windows, the registry.</summary>
    public SystemSerialPorts(IFileSystem fs)
        : this(fs, OperatingSystem.IsWindows(), WindowsSerialPortNames.Read) { }

    /// <summary>
    /// Uses <paramref name="fs"/> for device files and
    /// <paramref name="windowsPortNames"/> for the COM ports Windows lists.
    /// </summary>
    public SystemSerialPorts(
        IFileSystem fs,
        bool windows,
        Func<IReadOnlyCollection<string>> windowsPortNames
    )
    {
        _fs = fs;
        _windows = windows;
        _windowsPortNames = windowsPortNames;
    }

    /// <inheritdoc/>
    public string Resolve(string port)
    {
        if (_windows)
        {
            return WindowsName(port);
        }
        var file = _fs.FileInfo.New(_fs.Path.GetFullPath(port));
        if (file.LinkTarget is null)
        {
            return file.FullName;
        }
        try
        {
            return file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return file.FullName;
        }
    }

    /// <inheritdoc/>
    public bool Exists(string port) =>
        _windows
            ? _windowsPortNames().Any(name => WindowsName(name) == WindowsName(port))
            : _fs.File.Exists(port);

    private static string WindowsName(string port) =>
        (
            port.StartsWith(WindowsDevicePrefix, StringComparison.Ordinal)
                ? port[WindowsDevicePrefix.Length..]
                : port
        ).ToUpperInvariant();
}
