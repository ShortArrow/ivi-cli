using System.Diagnostics;
using IviCli.Application.Servers;

namespace IviCli.Infrastructure.Servers;

/// <summary>
/// <see cref="IProcessLiveness"/> that asks the operating system whether a
/// process with the id exists.
/// </summary>
public sealed class OsProcessLiveness : IProcessLiveness
{
    /// <inheritdoc/>
    public bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
