namespace IviCli.Application.Servers;

/// <summary>
/// Port that says whether an operating-system process is running, for a
/// process id read from the <see cref="IServerProcessRegistry"/>.
/// </summary>
public interface IProcessLiveness
{
    /// <summary>Whether a process with <paramref name="processId"/> is running.</summary>
    bool IsAlive(int processId);
}
