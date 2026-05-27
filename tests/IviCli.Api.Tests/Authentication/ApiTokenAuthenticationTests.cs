using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using IviCli.Api.Authentication;
using IviCli.Application.Auth;
using IviCli.Domain.Auth;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests.Authentication;

public sealed class ApiTokenAuthenticationTests
{
    private static FakeApiTokenStore StoreWithToken(out string rawToken)
    {
        var handler = new CreateApiTokenCommandHandler(new FakeApiTokenStore());
        var report = handler
            .HandleAsync(new CreateApiTokenCommand("test-token"), default)
            .GetAwaiter()
            .GetResult()
            .ShouldBeOk();
        rawToken = report.Token;
        return new FakeApiTokenStore(new ApiTokenDocument(ImmutableArray.Create(report.Stored)));
    }

    [Fact]
    public async Task Healthz_is_accessible_without_token_even_when_anonymous_disabled()
    {
        var store = StoreWithToken(out _);
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = false, AllowAnonymous = false }
        );

        var resp = await host.Client.GetAsync("/healthz");

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_token_with_tokens_configured_returns_401()
    {
        var store = StoreWithToken(out _);
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = false, AllowAnonymous = false }
        );

        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Invalid_token_returns_401()
    {
        var store = StoreWithToken(out _);
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = false, AllowAnonymous = false }
        );
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "ivicli_pat_INVALID"
        );

        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Valid_token_returns_200()
    {
        var store = StoreWithToken(out var raw);
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = false, AllowAnonymous = false }
        );
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            raw
        );

        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Empty_store_with_AllowAnonymous_lets_request_through()
    {
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = true }
        );

        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Empty_store_without_AllowAnonymous_blocks_request()
    {
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            authOptions: new ApiAuthenticationOptions { IsLoopback = false, AllowAnonymous = false }
        );

        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
