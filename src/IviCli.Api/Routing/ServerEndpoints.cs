using IviCli.Api.Mapping;
using IviCli.Application.Servers;
using IviCli.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IviCli.Api.Routing;

/// <summary>Maps the server-resource routes under <c>/v1/servers</c>.</summary>
public static class ServerEndpoints
{
    /// <summary>Attaches the GET endpoint to the supplied router.</summary>
    public static IEndpointRouteBuilder MapServers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/servers", ListServers).WithName("ListServers");
        return app;
    }

    private static async Task<IResult> ListServers(
        ListServersQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new ListServersQuery(), ct);
        return result switch
        {
            Result<ServerListing, ListServersError>.Ok ok => Results.Ok(ok.Value.ToDto()),
            Result<ServerListing, ListServersError>.Error err => ApiMapping.ProblemJson(
                StatusCodes.Status503ServiceUnavailable,
                "config_store_failure",
                err.Err.Message
            ),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
