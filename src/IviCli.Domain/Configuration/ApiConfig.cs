namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[api]</c> section of a configuration document. Currently only
/// houses the TLS sub-table (ADR 0039); future API-wide knobs (CORS,
/// rate limits, response caching) accrete here so the operator's mental
/// model stays "one section per surface."
/// </summary>
public sealed record ApiConfig(TlsConfig Tls)
{
    /// <summary>The TLS-disabled all-defaults <see cref="ApiConfig"/>.</summary>
    public static ApiConfig Default { get; } = new(TlsConfig.Default);
}
