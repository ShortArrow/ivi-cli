using IviCli.Domain;
using IviCli.Domain.Auth;

namespace IviCli.Application.Auth;

/// <summary>
/// Persistence port for the Management API token registry (ADR 0036).
/// Mirrors the shape of <see cref="IviCli.Application.Configuration.IConfigStore"/>:
/// load the whole document, save the whole document, atomic write.
/// "Token not found" is a handler concern; the store only surfaces
/// read / write IO failures.
/// </summary>
public interface IApiTokenStore
{
    /// <summary>Loads the persisted document. Missing file → <see cref="ApiTokenDocument.Empty"/>.</summary>
    Task<Result<ApiTokenDocument, ApiTokenStoreError>> LoadAsync(CancellationToken ct);

    /// <summary>Persists <paramref name="document"/> atomically.</summary>
    Task<Result<Unit, ApiTokenStoreError>> SaveAsync(
        ApiTokenDocument document,
        CancellationToken ct
    );
}

/// <summary>Errors the token store can surface.</summary>
public abstract record ApiTokenStoreError : IviError
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

/// <summary>The token file could not be read.</summary>
public sealed record ApiTokenStoreReadFailure(string Reason, Exception? Inner = null)
    : ApiTokenStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "api token store read failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}

/// <summary>The token file could not be written.</summary>
public sealed record ApiTokenStoreWriteFailure(string Reason, Exception? Inner = null)
    : ApiTokenStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "api token store write failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}
