using System.Runtime.InteropServices;
using Ivi.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Whether an installed VISA runtime can back the IVI shared components.
/// </summary>
/// <remarks>
/// <c>Ivi.Visa</c> ships with ivi-cli, but its native conflict manager,
/// <c>visaConfMgr</c>, comes only with a VISA runtime. Without it a call
/// to <see cref="GlobalResourceManager"/> fails, and the conflict manager
/// it created throws <see cref="DllNotFoundException"/> from its finalizer,
/// which ends the process whenever the garbage collector reaches it. The
/// adapters therefore do not call into <c>Ivi.Visa</c> unless the native
/// library loads.
/// </remarks>
internal static class VisaRuntime
{
    private const string ConflictManagerLibrary = "visaConfMgr.dll";

    private static readonly Lazy<bool> _installed = new(() =>
    {
        if (
            !NativeLibrary.TryLoad(
                ConflictManagerLibrary,
                typeof(GlobalResourceManager).Assembly,
                null,
                out var handle
            )
        )
        {
            return false;
        }
        NativeLibrary.Free(handle);
        return true;
    });

    /// <summary>True when <c>visaConfMgr</c> loads the way <c>Ivi.Visa</c> loads it.</summary>
    public static bool IsInstalled => _installed.Value;

    /// <summary>The detail every adapter reports when no runtime is installed.</summary>
    public const string MissingDetail =
        "no VISA implementation is registered; install a VISA runtime (e.g. NI-VISA or Keysight VISA)";
}
