using System.Net.Http.Json;
using IviCli.Api.Authentication;
using IviCli.Api.Contracts;
using IviCli.Application.Audit;
using IviCli.Application.Auth;
using IviCli.Domain;
using IviCli.Domain.Auth;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests;

/// <summary>
/// End-to-end audit emission tests (ADR 0043) — verifies the
/// Management API's auth middleware + audit middleware land the
/// expected events on the injected <see cref="IAuditLog"/>.
/// </summary>
public sealed class AuditLogTests
{
    [Fact]
    public async Task Healthz_request_emits_one_ApiRequest_event()
    {
        var audit = new RecordingAuditLog();
        await using var host = await ApiTestHost.StartAsync(ConfigDocument.Empty, auditLog: audit);

        var resp = await host.Client.GetAsync("/healthz");
        resp.IsSuccessStatusCode.ShouldBeTrue();

        var apiRequests = audit.Events.OfType<ApiRequest>().ToList();
        apiRequests.Count.ShouldBe(1);
        apiRequests[0].Method.ShouldBe("GET");
        apiRequests[0].Path.ShouldBe("/healthz");
        apiRequests[0].Status.ShouldBe(200);
    }

    [Fact]
    public async Task Invalid_bearer_token_emits_AuthFailed_then_ApiRequest_401()
    {
        var doc = ApiTokenDocument.Empty.Add(
            new ApiToken(
                Id: "abc",
                HashHex: CreateApiTokenCommandHandler.HashHex("ivicli_pat_real"),
                Label: "production",
                CreatedAt: DateTimeOffset.UtcNow,
                LastUsedAt: null
            )
        );
        var store = new FakeApiTokenStore(doc);
        var audit = new RecordingAuditLog();
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = false },
            auditLog: audit
        );

        host.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ivicli_pat_wrong");
        var resp = await host.Client.GetAsync("/v1/devices");
        ((int)resp.StatusCode).ShouldBe(401);

        var failures = audit.Events.OfType<AuthFailed>().ToList();
        failures.Count.ShouldBe(1);
        failures[0].Mechanism.ShouldBe("pat");
        failures[0].Reason.ShouldBe("invalid_token");

        var apiRequests = audit.Events.OfType<ApiRequest>().ToList();
        apiRequests.Count.ShouldBe(1);
        apiRequests[0].Status.ShouldBe(401);
    }

    [Fact]
    public async Task Valid_bearer_token_emits_AuthSucceeded_then_ApiRequest_200()
    {
        const string token = "ivicli_pat_real";
        var doc = ApiTokenDocument.Empty.Add(
            new ApiToken(
                Id: "abc",
                HashHex: CreateApiTokenCommandHandler.HashHex(token),
                Label: "production",
                CreatedAt: DateTimeOffset.UtcNow,
                LastUsedAt: null
            )
        );
        var store = new FakeApiTokenStore(doc);
        var audit = new RecordingAuditLog();
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = false },
            auditLog: audit
        );

        host.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var resp = await host.Client.GetAsync("/v1/devices");
        ((int)resp.StatusCode).ShouldBe(200);

        var successes = audit.Events.OfType<AuthSucceeded>().ToList();
        successes.Count.ShouldBe(1);
        successes[0].Subject.ShouldBe("production");
        successes[0].Mechanism.ShouldBe("pat");
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();

        public Task AppendAsync(AuditEvent ev, CancellationToken ct)
        {
            lock (Events)
            {
                Events.Add(ev);
            }
            return Task.CompletedTask;
        }
    }
}
