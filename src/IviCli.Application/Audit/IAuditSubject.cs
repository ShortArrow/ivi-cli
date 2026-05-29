namespace IviCli.Application.Audit;

/// <summary>
/// Resolves the actor string emitted as <see cref="ConfigMutated.Subject"/>
/// and <see cref="ServerLifecycle.Subject"/>. Implementations decide the
/// convention (e.g. <c>cli/{user}</c>, <c>api/{token-label}</c>); ADR 0043
/// (Batch U) commits the CLI to <c>cli/{Environment.UserName}</c>.
/// </summary>
public interface IAuditSubject
{
    /// <summary>Returns the subject string for the current invocation context.</summary>
    string Get();
}

/// <summary>
/// Constant-subject implementation used when no contextual user is
/// available (tests, sinks that never persist subjects). Returns the
/// value passed at construction.
/// </summary>
public sealed class StaticAuditSubject : IAuditSubject
{
    private readonly string _value;

    /// <summary>Creates a static subject. <paramref name="value"/> is returned verbatim.</summary>
    public StaticAuditSubject(string value)
    {
        _value = value;
    }

    /// <inheritdoc/>
    public string Get() => _value;
}
