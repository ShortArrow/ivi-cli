using IviCli.Application.Audit;

namespace IviCli.Cli.Audit;

/// <summary>
/// CLI-side <see cref="IAuditSubject"/> returning <c>cli/{Environment.UserName}</c>.
/// Captures the OS user that invoked <c>ivicli</c> so audit consumers can
/// attribute mutations and server lifecycle events to a human operator
/// (ADR 0043 §Subject, Batch U).
/// </summary>
public sealed class CliAuditSubject : IAuditSubject
{
    /// <inheritdoc/>
    public string Get() => $"cli/{Environment.UserName}";
}
