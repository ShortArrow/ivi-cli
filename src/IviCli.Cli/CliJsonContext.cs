using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Cli.Commands;
using IviCli.Cli.Watch;

namespace IviCli.Cli;

/// <summary>Row of <c>api token list --json</c>.</summary>
internal sealed record ApiTokenView(
    string Id,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    string[] Scopes,
    DateTimeOffset? ExpiresAt
);

/// <summary>Row of <c>driver list --json</c>.</summary>
internal sealed record DriverView(
    string Name,
    string? Description,
    string? ModulePath,
    string? Prefix
);

/// <summary>Row of <c>logical list --json</c>.</summary>
internal sealed record LogicalNameView(string Name, string? Description, string? Session);

/// <summary>Row of <c>visa lint --json</c>.</summary>
internal sealed record LintFindingView(int Line, string Severity, string Message, string Snippet);

/// <summary>One <c>visa monitor --json</c> sample line.</summary>
internal sealed record MonitorSampleView(DateTimeOffset Ts, int Seq, string Query, string Response);

/// <summary>
/// Source-generated serializer for the CLI's <c>--json</c> outputs
/// (issue #15). Web casing throughout — the keys the commands printed
/// before were already lower/camel case, so the rendered lines are
/// byte-identical.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiTokenView[]))]
[JsonSerializable(typeof(DriverView[]))]
[JsonSerializable(typeof(LogicalNameView[]))]
[JsonSerializable(typeof(LintFindingView[]))]
[JsonSerializable(typeof(MonitorSampleView))]
[JsonSerializable(typeof(MockReceivedWritesCommand.WriteView[]))]
[JsonSerializable(typeof(NdjsonSink.WatchTickDto))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
