using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Xml.Linq;
using IviCli.Application.Drivers;
using IviCli.Domain;
using IviCli.Domain.Drivers;

namespace IviCli.Infrastructure.Drivers;

/// <summary>
/// Filesystem-backed <see cref="IIviConfigurationStore"/> that parses
/// the IVI Foundation standard <c>IviConfigurationStore.xml</c>
/// (ADR 0045).
///
/// The store is a tree of <c>&lt;SoftwareModule&gt;</c>,
/// <c>&lt;DriverSession&gt;</c>, and <c>&lt;LogicalName&gt;</c>
/// elements (plus hardware assets which we ignore today). This
/// reader is intentionally permissive — newer IVI versions add
/// fields, older versions omit some — so a missing optional element
/// yields <see langword="null"/> on the corresponding VO field
/// rather than a parse error.
///
/// XML namespace prefixes are stripped via <c>LocalName</c> matching
/// so the parser works across IVI versions that change the schema
/// URI without breaking the structure.
/// </summary>
public sealed class XmlIviConfigurationStore : IIviConfigurationStore
{
    private const string SoftwareModuleElement = "SoftwareModule";
    private const string LogicalNameElement = "LogicalName";
    private const string NameElement = "Name";
    private const string DescriptionElement = "Description";
    private const string ModulePathElement = "ModulePath";
    private const string PrefixElement = "Prefix";
    private const string SessionElement = "Session";

    private readonly IFileSystem _fs;
    private readonly string _path;

    /// <summary>Creates a store rooted at <paramref name="path"/>.</summary>
    public XmlIviConfigurationStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <inheritdoc/>
    public async Task<
        Result<ImmutableArray<IviDriver>, IviConfigurationStoreError>
    > ListDriversAsync(CancellationToken ct)
    {
        var docResult = await LoadAsync(ct).ConfigureAwait(false);
        if (docResult is not Result<XDocument, IviConfigurationStoreError>.Ok { Value: var doc })
        {
            return Result.Failure<ImmutableArray<IviDriver>, IviConfigurationStoreError>(
                ((Result<XDocument, IviConfigurationStoreError>.Error)docResult).Err
            );
        }

        var drivers = doc.Root is null
            ? ImmutableArray<IviDriver>.Empty
            : doc
                .Root.Descendants()
                .Where(e =>
                    string.Equals(e.Name.LocalName, SoftwareModuleElement, StringComparison.Ordinal)
                )
                .Select(ParseDriver)
                .Where(d => d is not null)
                .Select(d => d!)
                .ToImmutableArray();

        return Result.Success<ImmutableArray<IviDriver>, IviConfigurationStoreError>(drivers);
    }

    /// <inheritdoc/>
    public async Task<
        Result<ImmutableArray<IviLogicalName>, IviConfigurationStoreError>
    > ListLogicalNamesAsync(CancellationToken ct)
    {
        var docResult = await LoadAsync(ct).ConfigureAwait(false);
        if (docResult is not Result<XDocument, IviConfigurationStoreError>.Ok { Value: var doc })
        {
            return Result.Failure<ImmutableArray<IviLogicalName>, IviConfigurationStoreError>(
                ((Result<XDocument, IviConfigurationStoreError>.Error)docResult).Err
            );
        }

        var names = doc.Root is null
            ? ImmutableArray<IviLogicalName>.Empty
            : doc
                .Root.Descendants()
                .Where(e =>
                    string.Equals(e.Name.LocalName, LogicalNameElement, StringComparison.Ordinal)
                )
                .Select(ParseLogicalName)
                .Where(n => n is not null)
                .Select(n => n!)
                .ToImmutableArray();

        return Result.Success<ImmutableArray<IviLogicalName>, IviConfigurationStoreError>(names);
    }

    private async Task<Result<XDocument, IviConfigurationStoreError>> LoadAsync(
        CancellationToken ct
    )
    {
        if (!_fs.File.Exists(_path))
        {
            return Result.Failure<XDocument, IviConfigurationStoreError>(
                new IviConfigurationStoreNotFound(_path)
            );
        }

        string text;
        try
        {
            text = await _fs.File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<XDocument, IviConfigurationStoreError>(
                new IviConfigurationStoreReadFailure(_path, ex)
            );
        }

        try
        {
            return Result.Success<XDocument, IviConfigurationStoreError>(XDocument.Parse(text));
        }
        catch (System.Xml.XmlException ex)
        {
            return Result.Failure<XDocument, IviConfigurationStoreError>(
                new IviConfigurationStoreParseFailure($"XML parse error: {ex.Message}", ex)
            );
        }
    }

    private static IviDriver? ParseDriver(XElement element)
    {
        var name = ChildText(element, NameElement);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        return new IviDriver(
            name,
            ChildText(element, DescriptionElement),
            ChildText(element, ModulePathElement),
            ChildText(element, PrefixElement)
        );
    }

    private static IviLogicalName? ParseLogicalName(XElement element)
    {
        var name = ChildText(element, NameElement);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        return new IviLogicalName(
            name,
            ChildText(element, DescriptionElement),
            ChildText(element, SessionElement)
        );
    }

    private static string? ChildText(XElement parent, string localName)
    {
        var child = parent
            .Elements()
            .FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal)
            );
        if (child is null)
        {
            return null;
        }
        var text = child.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
