using System.Collections.Immutable;
using System.Globalization;
using System.IO.Abstractions;
using IviCli.Application.Auth;
using IviCli.Domain;
using IviCli.Domain.Auth;
using Tomlyn;
using Tomlyn.Model;

namespace IviCli.Infrastructure.Auth;

/// <summary>
/// File-backed <see cref="IApiTokenStore"/>. Persists the document to
/// a single TOML file with the atomic-write pattern other stores use
/// (write <c>.tmp</c>, move over). Missing file returns
/// <see cref="ApiTokenDocument.Empty"/> — operators have not yet
/// minted any tokens.
/// </summary>
public sealed class TomlApiTokenStore : IApiTokenStore
{
    private readonly IFileSystem _fs;
    private readonly string _path;

    /// <summary>Creates a store rooted at <paramref name="path"/>.</summary>
    public TomlApiTokenStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <inheritdoc/>
    public async Task<Result<ApiTokenDocument, ApiTokenStoreError>> LoadAsync(CancellationToken ct)
    {
        try
        {
            if (!_fs.File.Exists(_path))
            {
                return Result.Success<ApiTokenDocument, ApiTokenStoreError>(ApiTokenDocument.Empty);
            }
            var text = await _fs.File.ReadAllTextAsync(_path, ct);
            var model = Toml.ToModel(text);
            var tokens = ImmutableArray.CreateBuilder<ApiToken>();
            if (model.TryGetValue("token", out var raw) && raw is TomlTableArray array)
            {
                foreach (var t in array)
                {
                    tokens.Add(Read(t));
                }
            }
            return Result.Success<ApiTokenDocument, ApiTokenStoreError>(
                new ApiTokenDocument(tokens.ToImmutable())
            );
        }
        catch (Exception ex)
        {
            return Result.Failure<ApiTokenDocument, ApiTokenStoreError>(
                new ApiTokenStoreReadFailure(ex.Message, ex)
            );
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, ApiTokenStoreError>> SaveAsync(
        ApiTokenDocument document,
        CancellationToken ct
    )
    {
        try
        {
            var directory = _fs.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !_fs.Directory.Exists(directory))
            {
                _fs.Directory.CreateDirectory(directory);
            }
            var tmp = _path + ".tmp";
            await _fs.File.WriteAllTextAsync(tmp, Serialize(document), ct);
            if (_fs.File.Exists(_path))
            {
                _fs.File.Delete(_path);
            }
            _fs.File.Move(tmp, _path);
            return Result.Success<Unit, ApiTokenStoreError>(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result.Failure<Unit, ApiTokenStoreError>(
                new ApiTokenStoreWriteFailure(ex.Message, ex)
            );
        }
    }

    private static ApiToken Read(TomlTable table)
    {
        var id = (string)table["id"];
        var hash = (string)table["hash"];
        var label = table.TryGetValue("label", out var labelObj) ? (string)labelObj : "";
        var createdAt = DateTimeOffset.Parse(
            (string)table["createdAt"],
            CultureInfo.InvariantCulture
        );
        DateTimeOffset? lastUsedAt = null;
        if (table.TryGetValue("lastUsedAt", out var luObj) && luObj is string luStr)
        {
            lastUsedAt = DateTimeOffset.Parse(luStr, CultureInfo.InvariantCulture);
        }
        var scopes = ImmutableArray<string>.Empty;
        if (table.TryGetValue("scopes", out var scopesObj) && scopesObj is TomlArray scopesArray)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var entry in scopesArray)
            {
                if (entry is string s)
                {
                    builder.Add(s);
                }
            }
            scopes = builder.ToImmutable();
        }
        DateTimeOffset? expiresAt = null;
        if (table.TryGetValue("expiresAt", out var expObj) && expObj is string expStr)
        {
            expiresAt = DateTimeOffset.Parse(expStr, CultureInfo.InvariantCulture);
        }
        return new ApiToken(id, hash, label, createdAt, lastUsedAt, scopes, expiresAt);
    }

    private static string Serialize(ApiTokenDocument document)
    {
        var model = new TomlTable();
        var array = new TomlTableArray();
        foreach (var t in document.Tokens)
        {
            var table = new TomlTable
            {
                ["id"] = t.Id,
                ["hash"] = t.HashHex,
                ["label"] = t.Label,
                ["createdAt"] = t.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            };
            if (t.LastUsedAt is { } lu)
            {
                table["lastUsedAt"] = lu.ToString("O", CultureInfo.InvariantCulture);
            }
            if (!t.Scopes.IsDefaultOrEmpty)
            {
                var scopesArr = new TomlArray();
                foreach (var s in t.Scopes)
                {
                    scopesArr.Add(s);
                }
                table["scopes"] = scopesArr;
            }
            if (t.ExpiresAt is { } exp)
            {
                table["expiresAt"] = exp.ToString("O", CultureInfo.InvariantCulture);
            }
            array.Add(table);
        }
        model["token"] = array;
        return Toml.FromModel(model);
    }
}
