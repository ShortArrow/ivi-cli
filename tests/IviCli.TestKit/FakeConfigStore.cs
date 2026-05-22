using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.TestKit;

/// <summary>
/// In-memory <see cref="IConfigStore"/> for tests. The current document lives
/// entirely in memory, with builder-style methods to inject load / save
/// failures for fault-injection scenarios.
/// </summary>
public sealed class FakeConfigStore : IConfigStore
{
    private ConfigDocument _document;
    private ConfigStoreError? _nextLoadFailure;
    private ConfigStoreError? _nextSaveFailure;

    /// <summary>Creates a store seeded with <see cref="ConfigDocument.Empty"/>.</summary>
    public FakeConfigStore()
        : this(ConfigDocument.Empty) { }

    /// <summary>Creates a store seeded with the supplied initial document.</summary>
    public FakeConfigStore(ConfigDocument initial)
    {
        _document = initial;
    }

    /// <inheritdoc/>
    public Task<Result<ConfigDocument, ConfigStoreError>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_nextLoadFailure is { } failure)
        {
            _nextLoadFailure = null;
            return Task.FromResult(Result.Failure<ConfigDocument, ConfigStoreError>(failure));
        }

        return Task.FromResult(Result.Success<ConfigDocument, ConfigStoreError>(_document));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ConfigStoreError>> SaveAsync(
        ConfigDocument document,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (_nextSaveFailure is { } failure)
        {
            _nextSaveFailure = null;
            return Task.FromResult(Result.Failure<Unit, ConfigStoreError>(failure));
        }

        _document = document;
        return Task.FromResult(Result.Success<Unit, ConfigStoreError>(Unit.Value));
    }

    /// <summary>Arrange that the next <see cref="LoadAsync"/> call fails with <see cref="ConfigStoreReadFailure"/>.</summary>
    public void FailNextLoadWith(string reason) =>
        _nextLoadFailure = new ConfigStoreReadFailure(reason);

    /// <summary>Arrange that the next <see cref="SaveAsync"/> call fails with <see cref="ConfigStoreWriteFailure"/>.</summary>
    public void FailNextSaveWith(string reason) =>
        _nextSaveFailure = new ConfigStoreWriteFailure(reason);
}
