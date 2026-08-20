using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IviCli.TestKit;

/// <summary>
/// Lazily-evaluated probes for external prerequisites that integration
/// tests depend on. Each probe runs at most once per test process and
/// caches the boolean result; downstream <see cref="RequiresAttribute"/>
/// uses the cached value to decide whether a test should run or be
/// skipped with a precise reason string.
/// </summary>
public static class PrereqProbe
{
    /// <summary>
    /// True when <c>python --version</c> exits 0 within the probe timeout.
    /// </summary>
    public static bool HasPython => _python.Value;

    /// <summary>
    /// True when <c>python -c "import pyvisa"</c> exits 0. Implies
    /// <see cref="HasPython"/>.
    /// </summary>
    public static bool HasPyVisa => _pyvisa.Value;

    /// <summary>
    /// True when <c>Ivi.Visa</c> can be reflection-loaded from the
    /// current process. Indicates that the IVI Shared Components
    /// (NI-VISA, Keysight IO Libraries, or compatible) are installed
    /// and discoverable on the assembly resolution path (ADR 0037).
    /// </summary>
    public static bool HasNiVisa => _niVisa.Value;

    /// <summary>
    /// True when the virtual USB mock instrument (ADR 0049; pid.codes
    /// VID 0x1209, PID 0x0001) is currently attached and visible to the
    /// installed VISA runtime. Implies <see cref="HasNiVisa"/>. Probed
    /// by reflection so the TestKit carries no VISA dependency.
    /// </summary>
    public static bool HasUsbMock => _usbMock.Value;

    /// <summary>
    /// Returns the names of every prerequisite in <paramref name="names"/>
    /// that is currently missing. Returns an empty array when all are
    /// satisfied. The order of the returned names matches the input.
    /// </summary>
    public static string[] Missing(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var missing = new List<string>();
        foreach (var name in names)
        {
            if (!IsSatisfied(name))
            {
                missing.Add(name);
            }
        }
        return missing.ToArray();
    }

    private static bool IsSatisfied(string name) =>
        name switch
        {
            "python" => HasPython,
            "pyvisa" => HasPyVisa,
            "ni-visa" => HasNiVisa,
            "usb-mock" => HasUsbMock,
            _ => false,
        };

    private static readonly Lazy<bool> _python = new(() => Probe(PythonExecutable, "--version"));

    private static readonly Lazy<bool> _pyvisa = new(() =>
        _python.Value && Probe(PythonExecutable, "-c", "import pyvisa")
    );

    private static readonly Lazy<bool> _niVisa = new(() =>
    {
        try
        {
            var assembly = System.Reflection.Assembly.Load("Ivi.Visa");
            return assembly is not null;
        }
        catch
        {
            return false;
        }
    });

    private static readonly Lazy<bool> _usbMock = new(() =>
    {
        if (!_niVisa.Value)
        {
            return false;
        }
        try
        {
            var manager = System
                .Reflection.Assembly.Load("Ivi.Visa")
                .GetType("Ivi.Visa.GlobalResourceManager");
            var find = manager?.GetMethod("Find", new[] { typeof(string) });
            var matches =
                find?.Invoke(null, new object[] { "USB?*::0x1209::0x0001::?*::INSTR" })
                as System.Collections.IEnumerable;
            return matches is not null && matches.GetEnumerator().MoveNext();
        }
        catch
        {
            // No runtime, no USB support, or zero matches (VISA's Find
            // throws on an empty result) — the mock is not attached.
            return false;
        }
    });

    private static string PythonExecutable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";

    private static bool Probe(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }
            // Drain output to avoid deadlocking the child on a full pipe.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup; ignore failures.
                }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            // Missing binary, sandbox restrictions, or any other launch
            // failure all collapse to "prereq not satisfied".
            return false;
        }
    }
}
