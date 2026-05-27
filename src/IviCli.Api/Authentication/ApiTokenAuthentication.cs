using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IviCli.Application.Auth;
using IviCli.Domain;
using IviCli.Domain.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Api.Authentication;

/// <summary>
/// Runtime options that decide how the authentication middleware
/// behaves. Populated by the CLI <c>api start</c> verb before
/// <c>app.Run()</c>; the middleware just enforces.
/// </summary>
public sealed class ApiAuthenticationOptions
{
    /// <summary>True when the listener bound to a loopback address.</summary>
    public bool IsLoopback { get; set; } = true;

    /// <summary>
    /// True when the operator opted in to running without tokens
    /// (typically on loopback, or with <c>--allow-anonymous</c> on
    /// non-loopback). When false, every request must carry a valid
    /// token.
    /// </summary>
    public bool AllowAnonymous { get; set; } = true;
}

/// <summary>
/// Token-based authentication middleware for the Management API
/// (ADR 0036). Validates <c>Authorization: Bearer &lt;token&gt;</c>
/// (HTTP) and the <c>ivi-cli-pat.&lt;token&gt;</c> sub-protocol
/// (WebSocket) against the persisted token hashes.
/// </summary>
public static class ApiTokenAuthentication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>WebSocket sub-protocol prefix carrying the API token.</summary>
    public const string WebSocketProtocolPrefix = "ivi-cli-pat.";

    /// <summary>Paths that bypass authentication entirely.</summary>
    private static readonly string[] BypassPaths = { "/healthz", "/openapi/v1.json" };

    /// <summary>Installs the middleware ahead of any route map.</summary>
    public static IApplicationBuilder UseApiTokenAuthentication(this WebApplication app)
    {
        app.Use(
            async (context, next) =>
            {
                if (ShouldBypass(context))
                {
                    await next();
                    return;
                }

                var options =
                    context.RequestServices.GetService<ApiAuthenticationOptions>()
                    ?? new ApiAuthenticationOptions();
                var store = context.RequestServices.GetRequiredService<IApiTokenStore>();
                var loaded = await store.LoadAsync(context.RequestAborted);
                if (
                    loaded
                    is not Result<ApiTokenDocument, ApiTokenStoreError>.Ok { Value: var document }
                )
                {
                    await WriteUnauthorizedAsync(
                        context,
                        "token_store_unavailable",
                        "the token store could not be read."
                    );
                    return;
                }

                // Empty-store fast path: when no tokens are configured the
                // bind decides — loopback / anonymous-opt-in → pass, else
                // demand a token (and reject).
                if (document.Tokens.IsDefaultOrEmpty)
                {
                    if (options.AllowAnonymous)
                    {
                        await next();
                        return;
                    }
                    await WriteUnauthorizedAsync(
                        context,
                        "unauthorized",
                        "no API tokens configured; create one with 'ivicli api token create'."
                    );
                    return;
                }

                var candidate = ExtractToken(context);
                if (string.IsNullOrEmpty(candidate))
                {
                    await WriteUnauthorizedAsync(
                        context,
                        "unauthorized",
                        "missing API token (use 'Authorization: Bearer <token>' or the 'ivi-cli-pat.<token>' WebSocket sub-protocol)."
                    );
                    return;
                }

                var candidateHash = CreateApiTokenCommandHandler.HashHex(candidate);
                ApiToken? match = null;
                foreach (var t in document.Tokens)
                {
                    if (FixedTimeHashEquals(candidateHash, t.HashHex))
                    {
                        match = t;
                        break;
                    }
                }
                if (match is null)
                {
                    await WriteUnauthorizedAsync(context, "unauthorized", "invalid API token.");
                    return;
                }

                // Best-effort last-used update — never fail the request if
                // the touch save fails.
                _ = store.SaveAsync(
                    document.TouchLastUsed(match.Id, DateTimeOffset.UtcNow),
                    CancellationToken.None
                );

                await next();
            }
        );
        return app;
    }

    private static bool ShouldBypass(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        foreach (var bypass in BypassPaths)
        {
            if (string.Equals(path, bypass, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ExtractToken(HttpContext context)
    {
        // HTTP path: Authorization: Bearer <token>
        if (
            context.Request.Headers.TryGetValue("Authorization", out var authValues)
            && authValues.Count > 0
        )
        {
            var auth = authValues[0]!;
            const string bearer = "Bearer ";
            if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            {
                var token = auth[bearer.Length..].Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    return token;
                }
            }
        }

        // WebSocket path: Sec-WebSocket-Protocol carries one or more
        // sub-protocols; we look for "ivi-cli-pat.<token>" entries.
        if (
            context.WebSockets.IsWebSocketRequest
            && context.WebSockets.WebSocketRequestedProtocols.Count > 0
        )
        {
            foreach (var proto in context.WebSockets.WebSocketRequestedProtocols)
            {
                if (proto.StartsWith(WebSocketProtocolPrefix, StringComparison.Ordinal))
                {
                    var token = proto[WebSocketProtocolPrefix.Length..];
                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }
            }
        }

        return null;
    }

    private static bool FixedTimeHashEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        var ba = Encoding.ASCII.GetBytes(a.ToLowerInvariant());
        var bb = Encoding.ASCII.GetBytes(b.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static async Task WriteUnauthorizedAsync(
        HttpContext context,
        string code,
        string message
    )
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { error = new { code, message } }, JsonOptions);
        await context.Response.WriteAsync(body);
    }
}
