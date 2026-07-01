using IviCli.Application.Backends;

namespace IviCli.TestKit;

/// <summary>
/// Deterministic <see cref="IEndpointProber"/> double for scan tests. Ports are
/// closed by default; <see cref="Open"/> marks a <c>(host, port)</c> as
/// reachable and optionally supplies an <c>*IDN?</c> response (returned only when
/// the caller requests identification, mirroring the real prober).
/// </summary>
public sealed class FakeEndpointProber : IEndpointProber
{
    private readonly Dictionary<(string Host, int Port), string?> _open = new();

    /// <summary>Marks <paramref name="host"/>:<paramref name="port"/> as open, with an optional IDN.</summary>
    public FakeEndpointProber Open(string host, int port, string? idn = null)
    {
        _open[(host, port)] = idn;
        return this;
    }

    /// <inheritdoc/>
    public Task<EndpointProbe> ProbeAsync(
        string host,
        int port,
        bool identify,
        CancellationToken ct
    )
    {
        if (!_open.TryGetValue((host, port), out var idn))
        {
            return Task.FromResult(new EndpointProbe(Open: false, Idn: null));
        }
        return Task.FromResult(new EndpointProbe(Open: true, Idn: identify ? idn : null));
    }
}
