using IviCli.Application.Servers;

namespace IviCli.TestKit;

/// <summary><see cref="IProcessLiveness"/> that reports only the given process ids as running.</summary>
public sealed class FakeProcessLiveness : IProcessLiveness
{
    private readonly HashSet<int> _alive;

    /// <summary>Creates a probe for which exactly <paramref name="alive"/> are running.</summary>
    public FakeProcessLiveness(params int[] alive)
    {
        _alive = [.. alive];
    }

    /// <inheritdoc/>
    public bool IsAlive(int processId) => _alive.Contains(processId);
}
