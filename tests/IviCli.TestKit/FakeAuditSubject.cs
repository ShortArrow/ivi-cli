using IviCli.Application.Audit;

namespace IviCli.TestKit;

/// <summary>
/// Deterministic <see cref="IAuditSubject"/> for tests. Defaults to the
/// literal string <c>"test"</c>; callers may supply a different fixed
/// value to assert that a specific subject is forwarded through a
/// handler.
/// </summary>
public sealed class FakeAuditSubject : IAuditSubject
{
    private readonly string _value;

    /// <summary>Creates a subject that always returns the supplied value.</summary>
    public FakeAuditSubject(string value = "test")
    {
        _value = value;
    }

    /// <inheritdoc/>
    public string Get() => _value;
}
