using System.Collections.Immutable;
using System.IO.Abstractions;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.Infrastructure.Plugins;
using IviCli.Plugin;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Plugins;

/// <summary>
/// End-to-end plugin loader + registration tests (ADR 0013).
/// Stages the IviCli.Plugin.Sample DLL into a temporary directory
/// with a hand-written plugin.toml, points <see cref="PluginLoader"/>
/// at it, drives the resolved <see cref="IIviPlugin.Register"/>
/// against a real <see cref="PluginServices"/>, then asserts the
/// <see cref="PluginBackendFactory"/> routes a matching device
/// resource to the plugin's backend instance.
/// </summary>
public sealed class PluginLoaderE2ETests
{
    [Fact]
    public async Task Sample_plugin_round_trips_through_loader_and_factory()
    {
        // 1. Copy the sample plugin DLL into a temporary plugins/<name>/ dir.
        var sampleDll =
            typeof(IviCli.Plugin.Sample.SampleAcmePlugin).Assembly.Location
            ?? throw new InvalidOperationException("sample plugin assembly location missing");
        var sampleDir = Path.GetDirectoryName(sampleDll)!;
        using var temp = new TempPluginsRoot();
        var pluginDir = Path.Combine(temp.Root, "acme-instruments");
        Directory.CreateDirectory(pluginDir);
        File.Copy(sampleDll, Path.Combine(pluginDir, "IviCli.Plugin.Sample.dll"));
        // Copy every other DLL the sample plugin transitively needs so
        // AssemblyLoadContext can resolve them.
        foreach (var dll in Directory.EnumerateFiles(sampleDir, "*.dll"))
        {
            var dest = Path.Combine(pluginDir, Path.GetFileName(dll));
            if (!File.Exists(dest))
            {
                File.Copy(dll, dest);
            }
        }
        File.WriteAllText(
            Path.Combine(pluginDir, "plugin.toml"),
            $"""
            [plugin]
            name = "acme-instruments"
            version = "1.0.0"
            target_api_version = {HostApiVersion.Current}
            entry_point = "IviCli.Plugin.Sample.SampleAcmePlugin"
            assembly = "IviCli.Plugin.Sample.dll"
            """
        );

        // 2. Run the loader against the temp dir.
        var loader = new PluginLoader(new FileSystem());
        var cfg = PluginsConfig.From(true, ImmutableArray<string>.Empty).ShouldBeOk();
        var loaded = loader.LoadAll(cfg, temp.Root);
        loaded.Count.ShouldBe(1);
        loaded[0].Manifest.Name.ShouldBe("acme-instruments");

        // 3. Drive Register against a PluginServices bound to a real
        //    IServiceCollection, build a provider, and resolve the
        //    plugin-contributed backend via PluginBackendFactory.
        var services = new ServiceCollection();
        var pluginServices = new PluginServices(services);
        loaded[0].Instance.Register(pluginServices);
        pluginServices.Registrations.Count.ShouldBe(1);
        var sp = services.BuildServiceProvider();

        var inner = new ConstantBackendFactory(new InnerSentinelBackend());
        var factory = new PluginBackendFactory(inner, sp, pluginServices.Registrations);

        var acmeDevice = new Device(
            DeviceName.From("acme1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.168.0.5::acme0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );
        var backend = factory.CreateFor(acmeDevice).ShouldBeOk();
        var idn = (
            await backend.QueryAsync(acmeDevice, ScpiQuery.From("*IDN?").ShouldBeOk(), default)
        ).ShouldBeOk();
        idn.ShouldBe("ACME,X100,42,1.0");

        // 4. A non-matching device falls through to the inner factory.
        var otherDevice = new Device(
            DeviceName.From("other").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.168.0.5::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );
        factory.CreateFor(otherDevice).ShouldBeOk().ShouldBeOfType<InnerSentinelBackend>();
    }

    private sealed class ConstantBackendFactory : IBackendFactory
    {
        private readonly IIviBackend _value;

        public ConstantBackendFactory(IIviBackend value) => _value = value;

        public Result<IIviBackend, BackendError> CreateFor(Device device) =>
            Result.Success<IIviBackend, BackendError>(_value);
    }

    private sealed class InnerSentinelBackend : IIviBackend
    {
        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<string, BackendError>("inner"));

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<string, BackendError>("inner"));

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

#pragma warning disable CS1998
        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            yield break;
        }
#pragma warning restore CS1998
    }

    private sealed class TempPluginsRoot : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "ivi-plugin-tests-" + Guid.NewGuid().ToString("N"));

        public TempPluginsRoot() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }
}
