using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Api.Contracts;

namespace IviCli.Api;

/// <summary>Body of <c>GET /healthz</c>.</summary>
public sealed record HealthzDto(string Status);

/// <summary>
/// Source-generated serializer for the hand-written API responses —
/// the error envelope and the health probe (issue #15). Endpoint DTOs
/// bound by the minimal-API pipeline are not here; they move when the
/// AOT publish flavor lands.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ErrorDto))]
[JsonSerializable(typeof(HealthzDto))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
