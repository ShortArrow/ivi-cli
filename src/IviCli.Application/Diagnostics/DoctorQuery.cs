using System.Collections.Immutable;
using IviCli.Domain;

namespace IviCli.Application.Diagnostics;

/// <summary>Query DTO for <c>ivicli doctor</c> (PRD §6.4).</summary>
public sealed record DoctorQuery;

/// <summary>The overall diagnostics report.</summary>
/// <param name="Checks">The individual checks performed, in stable order.</param>
public sealed record DiagnosticsReport(ImmutableArray<DiagnosticCheck> Checks);

/// <summary>One named environment check and its outcome.</summary>
/// <param name="Name">Short identifier (e.g. <c>dotnet</c>, <c>config</c>).</param>
/// <param name="Status">The check's outcome bucket.</param>
/// <param name="Detail">Human-readable explanation of the outcome.</param>
public sealed record DiagnosticCheck(string Name, DiagnosticStatus Status, string Detail);

/// <summary>The status bucket reported by a <see cref="DiagnosticCheck"/>.</summary>
public enum DiagnosticStatus
{
    /// <summary>Check completed without any issue.</summary>
    Ok,

    /// <summary>Check identified a non-blocking concern.</summary>
    Warning,

    /// <summary>Check identified a blocking problem.</summary>
    Error,
}

/// <summary>Errors that the doctor query itself can fail with (rare — most issues are reported as DiagnosticCheck entries).</summary>
public abstract record DoctorError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}
