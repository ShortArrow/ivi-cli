using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Fake;

/// <summary>
/// In-memory scanner companion for <see cref="FakeBackend"/>. Tests can stage
/// discoverable resources via <see cref="Register(VisaResource, string?)"/>;
/// the default empty list mirrors the production behaviour of "no real
/// backend, no discovery".
/// </summary>
public sealed class FakeBackendScanner : IBackendScanner
{
    private readonly List<DiscoveredResource> _resources = new();
    private BackendError? _nextFailure;

    /// <summary>Registers a resource that subsequent scans will report.</summary>
    public FakeBackendScanner Register(VisaResource resource, string? idn = null)
    {
        _resources.Add(new DiscoveredResource(resource, idn));
        return this;
    }

    /// <summary>Arranges that the next <see cref="ScanAsync"/> reports a failure.</summary>
    public FakeBackendScanner FailNextWith(BackendError failure)
    {
        _nextFailure = failure;
        return this;
    }

    /// <inheritdoc/>
    public Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (_nextFailure is { } failure)
        {
            _nextFailure = null;
            return Task.FromResult(
                Result.Failure<ImmutableArray<DiscoveredResource>, BackendError>(failure)
            );
        }

        return Task.FromResult(
            Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
                _resources.ToImmutableArray()
            )
        );
    }
}
