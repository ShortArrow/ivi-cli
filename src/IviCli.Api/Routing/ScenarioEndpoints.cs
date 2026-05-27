using IviCli.Api.Mapping;
using IviCli.Application.Mock;
using IviCli.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IviCli.Api.Routing;

/// <summary>Maps the scenario-resource routes under <c>/v1/scenarios</c>.</summary>
public static class ScenarioEndpoints
{
    /// <summary>Attaches the GET endpoint to the supplied router.</summary>
    public static IEndpointRouteBuilder MapScenarios(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/scenarios", ListScenarios).WithName("ListScenarios");
        return app;
    }

    private static async Task<IResult> ListScenarios(
        ListScenariosQueryHandler handler,
        CancellationToken ct
    )
    {
        var result = await handler.HandleAsync(new ListScenariosQuery(), ct);
        return result switch
        {
            Result<ScenarioListing, ListScenariosError>.Ok ok => Results.Ok(ok.Value.ToDto()),
            Result<ScenarioListing, ListScenariosError>.Error err => ApiMapping.ProblemJson(
                StatusCodes.Status503ServiceUnavailable,
                "scenario_store_failure",
                err.Err.Message
            ),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
