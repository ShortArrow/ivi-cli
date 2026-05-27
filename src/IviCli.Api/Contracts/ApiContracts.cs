using System.Collections.Generic;

namespace IviCli.Api.Contracts;

/// <summary>
/// JSON DTOs surfaced by the Management API (ADR 0034). These records
/// are intentionally distinct from CLI stdout shapes so the OpenAPI
/// contract is stable independently of the human-facing CLI output.
/// </summary>
public static class ApiContracts
{
    // The class exists only as a namespace marker — every contract is a
    // sibling record below.
}

/// <summary>Wraps a single device's static metadata for list / status responses.</summary>
public sealed record DeviceDto(string Name, string Resource, int TimeoutMs);

/// <summary>Response body for <c>GET /v1/devices</c>.</summary>
public sealed record DeviceListingDto(IReadOnlyList<DeviceDto> Devices, string? Default);

/// <summary>Response body for <c>GET /v1/devices/{name}/status</c>.</summary>
public sealed record DeviceStatusDto(
    DeviceDto Device,
    bool Online,
    long LatencyMs,
    string? Idn,
    string? Error
);

/// <summary>Wraps a single server entry for list responses.</summary>
public sealed record ServerDto(string Name, string Type, string Bind, int Port);

/// <summary>Response body for <c>GET /v1/servers</c>.</summary>
public sealed record ServerListingDto(IReadOnlyList<ServerDto> Servers);

/// <summary>Response body for <c>GET /v1/scenarios</c>.</summary>
public sealed record ScenarioListingDto(IReadOnlyList<string> Scenarios);

/// <summary>Request body for <c>POST /v1/devices/{name}/query</c> and <c>/write</c>.</summary>
public sealed record ScpiRequestDto(string Scpi);

/// <summary>Response body for <c>POST /v1/devices/{name}/query</c>.</summary>
public sealed record ScpiQueryResponseDto(string Response);

/// <summary>Response body for <c>POST /v1/devices/{name}/write</c>.</summary>
public sealed record ScpiAckDto(bool Ok = true);

/// <summary>Common error envelope for every non-2xx response.</summary>
public sealed record ErrorDto(ErrorBodyDto Error);

/// <summary>Inner error body. <see cref="Code"/> is a stable machine-readable identifier.</summary>
public sealed record ErrorBodyDto(string Code, string Message);
