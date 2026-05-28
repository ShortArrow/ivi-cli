using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using IviCli.Api.Authentication;
using IviCli.Api.Contracts;
using IviCli.Application.Auth;
using IviCli.Domain;
using IviCli.Domain.Auth;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests;

/// <summary>
/// End-to-end PAT scope + expiry enforcement (ADR 0044). Each test
/// mints a token with specific scope / expiry shape and asserts the
/// auth middleware accepts or rejects the request with the expected
/// status code + audit reason.
/// </summary>
public sealed class ScopeExpiryTests
{
    private const string Raw = "ivicli_pat_real";

    private static ApiToken MakeToken(
        ImmutableArray<string> scopes = default,
        DateTimeOffset? expiresAt = null
    ) =>
        new(
            Id: "abc",
            HashHex: CreateApiTokenCommandHandler.HashHex(Raw),
            Label: "production",
            CreatedAt: DateTimeOffset.UtcNow,
            LastUsedAt: null,
            Scopes: scopes,
            ExpiresAt: expiresAt
        );

    [Fact]
    public async Task Expired_token_is_rejected_with_401_and_expired_token_audit()
    {
        var doc = ApiTokenDocument.Empty.Add(
            MakeToken(expiresAt: DateTimeOffset.UtcNow.AddDays(-1))
        );
        var store = new FakeApiTokenStore(doc);
        var audit = new TestAuditLog();
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = false },
            auditLog: audit
        );

        host.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Raw);
        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var fail = audit.Events.OfType<IviCli.Application.Audit.AuthFailed>().Single();
        fail.Reason.ShouldBe("expired_token");
    }

    [Fact]
    public async Task Scoped_token_lacking_write_scope_is_403_on_query()
    {
        var doc = ApiTokenDocument.Empty.Add(
            MakeToken(scopes: ImmutableArray.Create("read:devices"))
        );
        var store = new FakeApiTokenStore(doc);
        var audit = new TestAuditLog();
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = false },
            auditLog: audit
        );

        host.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Raw);
        var resp = await host.Client.PostAsJsonAsync(
            "/v1/devices/psu1/query",
            new ScpiRequestDto("*IDN?")
        );

        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var fail = audit.Events.OfType<IviCli.Application.Audit.AuthFailed>().Single();
        fail.Reason.ShouldBe("insufficient_scope");
    }

    [Fact]
    public async Task Scoped_token_with_read_devices_can_list_devices()
    {
        var doc = ApiTokenDocument.Empty.Add(
            MakeToken(scopes: ImmutableArray.Create("read:devices"))
        );
        var store = new FakeApiTokenStore(doc);
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = false }
        );

        host.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Raw);
        var resp = await host.Client.GetAsync("/v1/devices");

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Legacy_unrestricted_token_passes_every_scope_gate()
    {
        // Empty scopes = backward compat, every scope granted.
        var doc = ApiTokenDocument.Empty.Add(MakeToken());
        var store = new FakeApiTokenStore(doc);
        var device = new IviCli.Domain.Devices.Device(
            IviCli.Domain.Devices.DeviceName.From("psu1").ShouldBeOk(),
            IviCli.Domain.Visa.VisaResource.Parse("TCPIP0::1.2.3.4::inst0::INSTR").ShouldBeOk(),
            IviCli.Domain.Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var cfg = ConfigDocument.Empty.AddDevice(device).ShouldBeOk();
        var fakeBackend = new IviCli.Backends.Fake.FakeBackend().RespondToQuery(
            device.Name,
            "*IDN?",
            "FAKE"
        );
        await using var host = await ApiTestHost.StartAsync(
            cfg,
            backend: fakeBackend,
            tokenStore: store,
            authOptions: new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = false }
        );

        host.Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Raw);

        (await host.Client.GetAsync("/v1/devices")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await host.Client.PostAsJsonAsync("/v1/devices/psu1/query", new ScpiRequestDto("*IDN?"))
        ).IsSuccessStatusCode.ShouldBeTrue();
    }

    private sealed class TestAuditLog : IviCli.Application.Audit.IAuditLog
    {
        public List<IviCli.Application.Audit.AuditEvent> Events { get; } = new();

        public Task AppendAsync(IviCli.Application.Audit.AuditEvent ev, CancellationToken ct)
        {
            lock (Events)
            {
                Events.Add(ev);
            }
            return Task.CompletedTask;
        }
    }
}
