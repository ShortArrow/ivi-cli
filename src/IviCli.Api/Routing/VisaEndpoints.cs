using Microsoft.AspNetCore.Routing;

namespace IviCli.Api.Routing;

/// <summary>
/// Maps the <c>/v1/devices/{name}/{query|write}</c> SCPI verbs. v1
/// scaffold — full POST endpoints land in Batch I Task 2.
/// </summary>
public static class VisaEndpoints
{
    /// <summary>Attaches the SCPI verbs (none yet) to the supplied router.</summary>
    public static IEndpointRouteBuilder MapVisa(this IEndpointRouteBuilder app) => app;
}
