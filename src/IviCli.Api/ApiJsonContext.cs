using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Api.Contracts;

namespace IviCli.Api;

/// <summary>Body of <c>GET /healthz</c>.</summary>
public sealed record HealthzDto(string Status);

/// <summary>
/// Source-generated serializer for every JSON body the Management API
/// reads or writes (issue #15). The minimal-API pipeline resolves these
/// through <c>ConfigureHttpJsonOptions</c> in the builder, so request
/// binding and responses stay off the reflection path.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ErrorDto))]
[JsonSerializable(typeof(HealthzDto))]
[JsonSerializable(typeof(DeviceDto))]
[JsonSerializable(typeof(DeviceListingDto))]
[JsonSerializable(typeof(DeviceStatusDto))]
[JsonSerializable(typeof(ServerDto))]
[JsonSerializable(typeof(ServerListingDto))]
[JsonSerializable(typeof(ScenarioListingDto))]
[JsonSerializable(typeof(ScpiRequestDto))]
[JsonSerializable(typeof(ScpiQueryResponseDto))]
[JsonSerializable(typeof(ScpiAckDto))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
