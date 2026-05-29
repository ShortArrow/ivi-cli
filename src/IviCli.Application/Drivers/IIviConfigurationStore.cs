using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Drivers;

namespace IviCli.Application.Drivers;

/// <summary>
/// Application-side port for reading the IVI Configuration Store
/// (ADR 0045). Production implementation lives in Infrastructure
/// (<c>XmlIviConfigurationStore</c>) and parses the IVI Foundation
/// standard <c>IviConfigurationStore.xml</c>; tests inject the
/// <c>FakeIviConfigurationStore</c> double from TestKit.
/// </summary>
public interface IIviConfigurationStore
{
    /// <summary>Enumerates every installed driver in the store.</summary>
    Task<Result<ImmutableArray<IviDriver>, IviConfigurationStoreError>> ListDriversAsync(
        CancellationToken ct
    );

    /// <summary>Enumerates every logical name in the store.</summary>
    Task<Result<ImmutableArray<IviLogicalName>, IviConfigurationStoreError>> ListLogicalNamesAsync(
        CancellationToken ct
    );
}

/// <summary>Errors that <see cref="IIviConfigurationStore"/> implementations can surface.</summary>
public abstract record IviConfigurationStoreError : IviError
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

/// <summary>The configured store file does not exist on disk.</summary>
public sealed record IviConfigurationStoreNotFound(string Path) : IviConfigurationStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Information;

    /// <inheritdoc/>
    public override string Message =>
        "IVI Configuration Store not found at {Path}. On non-Windows hosts the store typically does not exist; "
        + "on Windows install the IVI Shared Components to populate it.";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Path };
}

/// <summary>The store file exists but could not be read (IO failure).</summary>
public sealed record IviConfigurationStoreReadFailure(string Path, Exception Inner)
    : IviConfigurationStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "IVI Configuration Store read failed at {Path}: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Path, Inner.Message };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}

/// <summary>The store file's XML could not be parsed into the expected schema.</summary>
public sealed record IviConfigurationStoreParseFailure(string Detail, Exception? Inner = null)
    : IviConfigurationStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "IVI Configuration Store parse failed: {Detail}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Detail };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}
