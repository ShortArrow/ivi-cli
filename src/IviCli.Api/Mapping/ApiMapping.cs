using IviCli.Api.Contracts;
using IviCli.Application.Devices;
using IviCli.Application.Mock;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using Microsoft.AspNetCore.Http;

namespace IviCli.Api.Mapping;

/// <summary>
/// Pure functions that turn Application records into API DTOs and
/// <see cref="Result{T, E}"/> outcomes into <see cref="IResult"/>
/// responses. Centralised so error-status mapping has one place to
/// audit (ADR 0034 §3 error contract).
/// </summary>
public static class ApiMapping
{
    /// <summary>Maps a <see cref="Device"/> to its API DTO.</summary>
    public static DeviceDto ToDto(this Device device) =>
        new(device.Name.Value, FormatResource(device.Resource), device.Timeout.Milliseconds);

    /// <summary>Maps a <see cref="DeviceListing"/> to its API DTO.</summary>
    public static DeviceListingDto ToDto(this DeviceListing listing) =>
        new(listing.Devices.Select(d => d.ToDto()).ToArray(), listing.DefaultDevice?.Value);

    /// <summary>Maps a <see cref="DeviceStatus"/> to its API DTO.</summary>
    public static DeviceStatusDto ToDto(this DeviceStatus status) =>
        new(
            status.Device.ToDto(),
            status.IsOnline,
            (long)status.ResponseTime.TotalMilliseconds,
            status.IdnResponse,
            status.FailureMessage
        );

    /// <summary>Maps a <see cref="Server"/> to its API DTO.</summary>
    public static ServerDto ToDto(this Server server) =>
        new(server.Name.Value, server.Type.ToString(), server.Bind.Value, server.Port.Value);

    /// <summary>Maps a <see cref="ServerListing"/> to its API DTO.</summary>
    public static ServerListingDto ToDto(this ServerListing listing) =>
        new(listing.Servers.Select(s => s.ToDto()).ToArray());

    /// <summary>Maps a <see cref="ScenarioListing"/> to its API DTO.</summary>
    public static ScenarioListingDto ToDto(this ScenarioListing listing) =>
        new(listing.Names.Select(n => n.Value).ToArray());

    /// <summary>Wraps an error code / message in the canonical envelope.</summary>
    public static ErrorDto Error(string code, string message) =>
        new(new ErrorBodyDto(code, message));

    /// <summary>Builds a JSON error response at the supplied status code.</summary>
    public static IResult ProblemJson(int status, string code, string message) =>
        Results.Json(Error(code, message), statusCode: status);

    private static string FormatResource(VisaResource resource) => resource.ToCanonical();
}
