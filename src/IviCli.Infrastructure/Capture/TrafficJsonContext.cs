using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Application.Capture;

namespace IviCli.Infrastructure.Capture;

/// <summary>
/// Source-generated serializer for capture NDJSON lines (issue #15),
/// shared by <see cref="NdjsonTrafficWriter"/> and
/// <see cref="NdjsonTrafficReader"/> so both directions stay one wire
/// format: web casing, enums as strings.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TrafficEvent))]
internal sealed partial class TrafficJsonContext : JsonSerializerContext;
