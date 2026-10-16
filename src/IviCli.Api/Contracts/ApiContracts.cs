using System.Collections.Generic;
using System.Text.Json.Serialization;

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

/// <summary>
/// Wraps a single device's static metadata for list / status responses.
/// <c>serial</c> appears only for a serial device, and each terminator only
/// when the device sets it (ADR 0055 §10).
/// </summary>
public sealed record DeviceDto(
    string Name,
    string Resource,
    int TimeoutMs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SerialLineDto? Serial = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ReadTermination = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? WriteTermination = null
);

/// <summary>
/// The settings a serial device's line opens with, named as in
/// <c>config.toml</c> (ADR 0055 §4): parity <c>none</c>, <c>odd</c>,
/// <c>even</c>, <c>mark</c> or <c>space</c>; stop bits 1, 1.5 or 2; flow
/// control <c>none</c>, <c>xon_xoff</c> or <c>rts_cts</c>.
/// </summary>
public sealed record SerialLineDto(
    int Baud,
    int DataBits,
    string Parity,
    double StopBits,
    string FlowControl
);

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
