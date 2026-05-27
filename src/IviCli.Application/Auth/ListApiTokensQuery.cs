using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Auth;

namespace IviCli.Application.Auth;

/// <summary>Query DTO for <c>ivicli api token list</c>.</summary>
public sealed record ListApiTokensQuery;

/// <summary>Application handler that reads the token document.</summary>
public sealed class ListApiTokensQueryHandler
{
    private readonly IApiTokenStore _store;

    /// <summary>Creates a new handler.</summary>
    public ListApiTokensQueryHandler(IApiTokenStore store)
    {
        _store = store;
    }

    /// <summary>Returns every persisted token (hash-only — safe to render).</summary>
    public async Task<Result<ImmutableArray<ApiToken>, ApiTokenStoreError>> HandleAsync(
        ListApiTokensQuery query,
        CancellationToken ct
    )
    {
        var result = await _store.LoadAsync(ct);
        return result switch
        {
            Result<ApiTokenDocument, ApiTokenStoreError>.Ok ok => Result.Success<
                ImmutableArray<ApiToken>,
                ApiTokenStoreError
            >(ok.Value.Tokens),
            Result<ApiTokenDocument, ApiTokenStoreError>.Error err => Result.Failure<
                ImmutableArray<ApiToken>,
                ApiTokenStoreError
            >(err.Err),
            _ => Result.Failure<ImmutableArray<ApiToken>, ApiTokenStoreError>(
                new ApiTokenStoreReadFailure("unknown result variant")
            ),
        };
    }
}
