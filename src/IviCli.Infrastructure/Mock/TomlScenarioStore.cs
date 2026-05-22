using System.Collections.Immutable;
using System.IO.Abstractions;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Infrastructure.Mock;

/// <summary>
/// File-backed <see cref="IScenarioStore"/> that persists each scenario as a
/// TOML file under a designated directory (ADR 0026 §2).
/// </summary>
public sealed class TomlScenarioStore : IScenarioStore
{
    private const string FileExtension = ".toml";

    private readonly IFileSystem _fs;
    private readonly string _directory;

    /// <summary>Creates a new TomlScenarioStore rooted at the supplied directory.</summary>
    public TomlScenarioStore(IFileSystem fs, string directory)
    {
        _fs = fs;
        _directory = directory;
    }

    /// <inheritdoc/>
    public Task<Result<ImmutableArray<ScenarioName>, ScenarioStoreError>> ListAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.Directory.Exists(_directory))
        {
            return Task.FromResult(
                Result.Success<ImmutableArray<ScenarioName>, ScenarioStoreError>(
                    ImmutableArray<ScenarioName>.Empty
                )
            );
        }

        string[] files;
        try
        {
            files = _fs.Directory.GetFiles(_directory, $"*{FileExtension}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Result.Failure<ImmutableArray<ScenarioName>, ScenarioStoreError>(
                    new ScenarioStoreReadFailure($"failed to list {_directory}", ex)
                )
            );
        }

        var names = ImmutableArray.CreateBuilder<ScenarioName>();
        foreach (var path in files.OrderBy(p => p, StringComparer.Ordinal))
        {
            var stem = _fs.Path.GetFileNameWithoutExtension(path);
            var nameResult = ScenarioName.From(stem);
            if (nameResult is Result<ScenarioName, ScenarioNameError>.Ok ok)
            {
                names.Add(ok.Value);
            }
            // Files whose stem fails ScenarioName validation are silently
            // ignored — they belong to the user / a future revision.
        }

        return Task.FromResult(
            Result.Success<ImmutableArray<ScenarioName>, ScenarioStoreError>(names.ToImmutable())
        );
    }

    /// <inheritdoc/>
    public async Task<Result<MockScenario, ScenarioStoreError>> LoadAsync(
        ScenarioName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var path = PathFor(name);
        if (!_fs.File.Exists(path))
        {
            return Result.Failure<MockScenario, ScenarioStoreError>(
                new ScenarioNotFound(name.Value)
            );
        }

        string text;
        try
        {
            text = await _fs.File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<MockScenario, ScenarioStoreError>(
                new ScenarioStoreReadFailure($"read failed at {path}", ex)
            );
        }

        return TomlScenarioParser.Parse(name, text);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, ScenarioStoreError>> SaveAsync(
        MockScenario scenario,
        bool overwriteIfExists,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var path = PathFor(scenario.Name);
        if (!overwriteIfExists && _fs.File.Exists(path))
        {
            return Result.Failure<Unit, ScenarioStoreError>(
                new ScenarioAlreadyExists(scenario.Name.Value)
            );
        }

        if (!_fs.Directory.Exists(_directory))
        {
            try
            {
                _fs.Directory.CreateDirectory(_directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Failure<Unit, ScenarioStoreError>(
                    new ScenarioStoreWriteFailure($"failed to create {_directory}", ex)
                );
            }
        }

        var serialized = TomlScenarioParser.Serialize(scenario);
        var tempPath = path + ".tmp";
        try
        {
            await _fs.File.WriteAllTextAsync(tempPath, serialized, ct);
            if (_fs.File.Exists(path))
            {
                _fs.File.Delete(path);
            }
            _fs.File.Move(tempPath, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Unit, ScenarioStoreError>(
                new ScenarioStoreWriteFailure($"write failed at {path}", ex)
            );
        }

        return Result.Success<Unit, ScenarioStoreError>(Unit.Value);
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ScenarioStoreError>> DeleteAsync(
        ScenarioName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var path = PathFor(name);
        if (!_fs.File.Exists(path))
        {
            return Task.FromResult(
                Result.Failure<Unit, ScenarioStoreError>(new ScenarioNotFound(name.Value))
            );
        }

        try
        {
            _fs.File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Result.Failure<Unit, ScenarioStoreError>(
                    new ScenarioStoreWriteFailure($"delete failed at {path}", ex)
                )
            );
        }

        return Task.FromResult(Result.Success<Unit, ScenarioStoreError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<bool, ScenarioStoreError>> ExistsAsync(
        ScenarioName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            Result.Success<bool, ScenarioStoreError>(_fs.File.Exists(PathFor(name)))
        );
    }

    private string PathFor(ScenarioName name) =>
        _fs.Path.Combine(_directory, name.Value + FileExtension);
}
