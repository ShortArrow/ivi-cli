using IviCli.Api.Contracts;
using IviCli.Api.Mapping;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Api.Routing;

/// <summary>Maps the device-resource routes under <c>/v1/devices</c>.</summary>
public static class DeviceEndpoints
{
    /// <summary>Attaches the GET + status endpoints to the supplied router.</summary>
    public static IEndpointRouteBuilder MapDevices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/devices", ListDevices).WithName("ListDevices");
        app.MapGet("/v1/devices/{name}/status", DeviceStatus).WithName("DeviceStatus");
        return app;
    }

    private static async Task<IResult> ListDevices(
        HttpContext ctx,
        ListDevicesQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new ListDevicesQuery(), ct);
        return result switch
        {
            Result<DeviceListing, ListDevicesError>.Ok ok => Results.Ok(ok.Value.ToDto()),
            Result<DeviceListing, ListDevicesError>.Error err => err.Err
            is ListDevicesStorageFailure
                ? ApiMapping.ProblemJson(
                    StatusCodes.Status503ServiceUnavailable,
                    "config_store_failure",
                    err.Err.Message
                )
                : ApiMapping.ProblemJson(
                    StatusCodes.Status500InternalServerError,
                    "internal_error",
                    err.Err.Message
                ),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> DeviceStatus(
        string name,
        StatusDeviceCommandHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new StatusDeviceCommand(name), ct);
        return result switch
        {
            Result<DeviceStatus, StatusDeviceError>.Ok ok => Results.Ok(ok.Value.ToDto()),
            Result<DeviceStatus, StatusDeviceError>.Error err => MapStatusError(err.Err, name),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static IResult MapStatusError(StatusDeviceError error, string name) =>
        error switch
        {
            StatusDeviceInvalidName or StatusDeviceUnknown or StatusDeviceNoTarget =>
                ApiMapping.ProblemJson(
                    StatusCodes.Status404NotFound,
                    "device_not_found",
                    $"device '{name}' is not registered."
                ),
            StatusDeviceConfigFailure or StatusDeviceSessionFailure => ApiMapping.ProblemJson(
                StatusCodes.Status503ServiceUnavailable,
                "config_store_failure",
                error.Message
            ),
            _ => ApiMapping.ProblemJson(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                error.Message
            ),
        };
}
