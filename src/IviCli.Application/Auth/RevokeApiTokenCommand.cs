using IviCli.Domain;
using IviCli.Domain.Auth;

namespace IviCli.Application.Auth;

/// <summary>Command DTO for <c>ivicli api token revoke &lt;id&gt;</c>.</summary>
public sealed record RevokeApiTokenCommand(string Id);

/// <summary>Application handler that removes a token by id.</summary>
public sealed class RevokeApiTokenCommandHandler
{
    private readonly IApiTokenStore _store;

    /// <summary>Creates a new handler.</summary>
    public RevokeApiTokenCommandHandler(IApiTokenStore store)
    {
        _store = store;
    }

    /// <summary>Returns the revoked token on success, or an error.</summary>
    public async Task<Result<ApiToken, RevokeApiTokenError>> HandleAsync(
        RevokeApiTokenCommand command,
        CancellationToken ct
    )
    {
        var loaded = await _store.LoadAsync(ct);
        if (loaded is not Result<ApiTokenDocument, ApiTokenStoreError>.Ok { Value: var document })
        {
            var err = ((Result<ApiTokenDocument, ApiTokenStoreError>.Error)loaded).Err;
            return Result.Failure<ApiToken, RevokeApiTokenError>(
                new RevokeApiTokenStoreFailure(err)
            );
        }
        ApiToken? target = null;
        foreach (var t in document.Tokens)
        {
            if (string.Equals(t.Id, command.Id, StringComparison.OrdinalIgnoreCase))
            {
                target = t;
                break;
            }
        }
        if (target is null)
        {
            return Result.Failure<ApiToken, RevokeApiTokenError>(
                new RevokeApiTokenUnknown(command.Id)
            );
        }
        var updated = document.Remove(target.Id) ?? document;
        var save = await _store.SaveAsync(updated, ct);
        if (save is Result<Unit, ApiTokenStoreError>.Error sErr)
        {
            return Result.Failure<ApiToken, RevokeApiTokenError>(
                new RevokeApiTokenStoreFailure(sErr.Err)
            );
        }
        return Result.Success<ApiToken, RevokeApiTokenError>(target);
    }
}

/// <summary>Errors the revoke handler can surface.</summary>
public abstract record RevokeApiTokenError : IviError
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

/// <summary>No token with the supplied id exists.</summary>
public sealed record RevokeApiTokenUnknown(string Id) : RevokeApiTokenError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no API token with id {Id}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Id };
}

/// <summary>Underlying store failed during load or save.</summary>
public sealed record RevokeApiTokenStoreFailure(ApiTokenStoreError Inner) : RevokeApiTokenError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}
