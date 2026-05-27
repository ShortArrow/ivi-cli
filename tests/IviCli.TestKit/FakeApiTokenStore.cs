using IviCli.Application.Auth;
using IviCli.Domain;
using IviCli.Domain.Auth;

namespace IviCli.TestKit;

/// <summary>
/// In-memory <see cref="IApiTokenStore"/> for tests. Mirrors
/// <see cref="FakeConfigStore"/>'s fault-injection surface
/// (<see cref="FailNextLoadWith"/> / <see cref="FailNextSaveWith"/>).
/// </summary>
public sealed class FakeApiTokenStore : IApiTokenStore
{
    private ApiTokenDocument _document;
    private ApiTokenStoreError? _nextLoadFailure;
    private ApiTokenStoreError? _nextSaveFailure;

    /// <summary>Creates an empty store.</summary>
    public FakeApiTokenStore()
        : this(ApiTokenDocument.Empty) { }

    /// <summary>Creates a store seeded with <paramref name="initial"/>.</summary>
    public FakeApiTokenStore(ApiTokenDocument initial)
    {
        _document = initial;
    }

    /// <summary>The current document (mirrors what would be on disk).</summary>
    public ApiTokenDocument Current => _document;

    /// <inheritdoc/>
    public Task<Result<ApiTokenDocument, ApiTokenStoreError>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_nextLoadFailure is { } failure)
        {
            _nextLoadFailure = null;
            return Task.FromResult(Result.Failure<ApiTokenDocument, ApiTokenStoreError>(failure));
        }
        return Task.FromResult(Result.Success<ApiTokenDocument, ApiTokenStoreError>(_document));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ApiTokenStoreError>> SaveAsync(
        ApiTokenDocument document,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (_nextSaveFailure is { } failure)
        {
            _nextSaveFailure = null;
            return Task.FromResult(Result.Failure<Unit, ApiTokenStoreError>(failure));
        }
        _document = document;
        return Task.FromResult(Result.Success<Unit, ApiTokenStoreError>(Unit.Value));
    }

    /// <summary>The next LoadAsync returns <see cref="ApiTokenStoreReadFailure"/>.</summary>
    public void FailNextLoadWith(string reason) =>
        _nextLoadFailure = new ApiTokenStoreReadFailure(reason);

    /// <summary>The next SaveAsync returns <see cref="ApiTokenStoreWriteFailure"/>.</summary>
    public void FailNextSaveWith(string reason) =>
        _nextSaveFailure = new ApiTokenStoreWriteFailure(reason);
}
