using IviCli.Api.Contracts;
using IviCli.Api.Mapping;
using IviCli.Application.Devices;
using IviCli.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IviCli.Api.Routing;

/// <summary>
/// Maps the SCPI verbs <c>POST /v1/devices/{name}/query</c> and
/// <c>POST /v1/devices/{name}/write</c>. Composes the existing
/// <see cref="QueryDeviceCommandHandler"/> /
/// <see cref="WriteDeviceCommandHandler"/> with the Result→IResult
/// mapping defined in <see cref="ApiMapping"/>.
/// </summary>
public static class VisaEndpoints
{
    /// <summary>Attaches the SCPI verb endpoints to the supplied router.</summary>
    public static IEndpointRouteBuilder MapVisa(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/devices/{name}/query", Query).WithName("DeviceQuery");
        app.MapPost("/v1/devices/{name}/write", Write).WithName("DeviceWrite");
        return app;
    }

    private static async Task<IResult> Query(
        string name,
        ScpiRequestDto? body,
        QueryDeviceCommandHandler handler,
        CancellationToken ct
    )
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Scpi))
        {
            return ApiMapping.ProblemJson(
                StatusCodes.Status400BadRequest,
                "missing_scpi",
                "request body must include a non-empty 'scpi' field."
            );
        }
        var result = await handler.HandleAsync(new QueryDeviceCommand(name, body.Scpi), ct);
        return result switch
        {
            Result<string, QueryDeviceError>.Ok ok => Results.Ok(
                new ScpiQueryResponseDto(ok.Value)
            ),
            Result<string, QueryDeviceError>.Error err => MapQueryError(err.Err, name),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> Write(
        string name,
        ScpiRequestDto? body,
        WriteDeviceCommandHandler handler,
        CancellationToken ct
    )
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Scpi))
        {
            return ApiMapping.ProblemJson(
                StatusCodes.Status400BadRequest,
                "missing_scpi",
                "request body must include a non-empty 'scpi' field."
            );
        }
        var result = await handler.HandleAsync(new WriteDeviceCommand(name, body.Scpi), ct);
        return result switch
        {
            Result<Unit, WriteDeviceError>.Ok => Results.Ok(new ScpiAckDto()),
            Result<Unit, WriteDeviceError>.Error err => MapWriteError(err.Err, name),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static IResult MapQueryError(QueryDeviceError error, string name) =>
        error switch
        {
            QueryDeviceInvalidScpi s => ApiMapping.ProblemJson(
                StatusCodes.Status400BadRequest,
                "invalid_scpi",
                s.Reason
            ),
            QueryDeviceInvalidName or QueryDeviceUnknown or QueryDeviceNoTarget =>
                ApiMapping.ProblemJson(
                    StatusCodes.Status404NotFound,
                    "device_not_found",
                    $"device '{name}' is not registered."
                ),
            QueryDeviceTransportFailure t => ApiMapping.ProblemJson(
                StatusCodes.Status502BadGateway,
                "backend_failure",
                t.Inner.Message
            ),
            QueryDeviceConfigFailure or QueryDeviceSessionFailure => ApiMapping.ProblemJson(
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

    private static IResult MapWriteError(WriteDeviceError error, string name) =>
        error switch
        {
            WriteDeviceInvalidScpi s => ApiMapping.ProblemJson(
                StatusCodes.Status400BadRequest,
                "invalid_scpi",
                s.Reason
            ),
            WriteDeviceInvalidName or WriteDeviceUnknown or WriteDeviceNoTarget =>
                ApiMapping.ProblemJson(
                    StatusCodes.Status404NotFound,
                    "device_not_found",
                    $"device '{name}' is not registered."
                ),
            WriteDeviceTransportFailure t => ApiMapping.ProblemJson(
                StatusCodes.Status502BadGateway,
                "backend_failure",
                t.Inner.Message
            ),
            WriteDeviceConfigFailure or WriteDeviceSessionFailure => ApiMapping.ProblemJson(
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
