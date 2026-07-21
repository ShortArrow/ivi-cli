using System.Reflection;
using IviCli.Application.Backends;
using IviCli.Application.Mock;
using IviCli.Application.Session;
using IviCli.Backends.Fake;
using IviCli.Server;
using IviCli.Server.HiSlip;
using IviCli.Server.Socket;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// Guards the composition root: each gateway takes its
/// <see cref="IScenarioBindingRefresher"/> as an optional constructor
/// dependency, so the real container must actually inject the registered
/// <see cref="SessionScenarioBindingRefresher"/> rather than fall back to the
/// no-op default. If it fell back, live re-binding would work in the
/// direct-construction tests yet silently do nothing in the shipped CLI.
/// </summary>
public sealed class GatewayScenarioRefresherWiringTests
{
    [Theory]
    [InlineData(typeof(SocketGatewayServer))]
    [InlineData(typeof(HiSlipGatewayServer))]
    [InlineData(typeof(Vxi11GatewayServer))]
    public void Gateway_receives_the_real_refresher_from_di(Type gatewayType)
    {
        using var provider = BuildProductionLikeProvider();

        var gateway = provider.GetRequiredService(gatewayType);

        RefresherField(gateway).ShouldBeOfType<SessionScenarioBindingRefresher>();
    }

    private static ServiceProvider BuildProductionLikeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        // The Fake backend registers IScenarioBindingRefresher →
        // SessionScenarioBindingRefresher (the real, session-backed impl).
        services.AddIviCliBackendsFake();
        services.AddSingleton<IScenarioStore>(new FakeScenarioStore());
        services.AddSingleton<ISessionStore>(new FakeSessionStore());
        services.AddSingleton<IBackendFactory>(sp => new FakeBackendFactory(
            sp.GetRequiredService<FakeBackend>()
        ));
        services.AddIviCliGatewayServers();
        return services.BuildServiceProvider();
    }

    private static object RefresherField(object gateway)
    {
        var field = gateway
            .GetType()
            .GetField("_refresher", BindingFlags.Instance | BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        var value = field.GetValue(gateway);
        value.ShouldNotBeNull();
        return value;
    }
}
