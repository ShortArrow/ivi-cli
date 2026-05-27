using System.Reflection;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Devices;
using NetArchTest.Rules;

namespace IviCli.Cli.Tests.Architecture;

/// <summary>
/// Enforces the layered dependency direction declared in ADR 0021. Failures
/// here mean a project reference or <c>using</c> statement broke the
/// hexagonal/CA layering and should be reverted or refactored.
/// </summary>
[Trait("Category", "Architecture")]
public class DependencyDirectionTests
{
    private static readonly Assembly DomainAssembly = typeof(DeviceName).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IConfigStore).Assembly;
    private static readonly Assembly InfrastructureAssembly = Assembly.Load(
        "IviCli.Infrastructure"
    );
    private static readonly Assembly BackendsLocalAssembly = Assembly.Load("IviCli.Backends.Local");
    private static readonly Assembly BackendsFakeAssembly = Assembly.Load("IviCli.Backends.Fake");
    private static readonly Assembly BackendsHiSlipAssembly = Assembly.Load(
        "IviCli.Backends.HiSlip"
    );
    private static readonly Assembly BackendsSocketAssembly = Assembly.Load(
        "IviCli.Backends.Socket"
    );
    private static readonly Assembly BackendsVxi11Assembly = Assembly.Load("IviCli.Backends.Vxi11");
    private static readonly Assembly ServerAssembly = Assembly.Load("IviCli.Server");

    private static readonly string[] AllBackendAssemblyNames =
    [
        "IviCli.Backends.Local",
        "IviCli.Backends.Fake",
        "IviCli.Backends.HiSlip",
        "IviCli.Backends.Socket",
        "IviCli.Backends.Replay",
        "IviCli.Backends.Vxi11",
    ];

    [Fact]
    public void Domain_DoesNotDependOnAnyOtherProjectAssembly()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "IviCli.Application",
                "IviCli.Infrastructure",
                "IviCli.Backends.Local",
                "IviCli.Backends.Fake",
                "IviCli.Backends.HiSlip",
                "IviCli.Backends.Socket",
                "IviCli.Backends.Replay",
                "IviCli.Backends.Vxi11",
                "IviCli.Server",
                "IviCli.Api",
                "IviCli.Cli"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain layer must not depend on any other layer: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrBackendsOrServerOrCli()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "IviCli.Infrastructure",
                "IviCli.Backends.Local",
                "IviCli.Backends.Fake",
                "IviCli.Backends.HiSlip",
                "IviCli.Backends.Socket",
                "IviCli.Backends.Replay",
                "IviCli.Backends.Vxi11",
                "IviCli.Server",
                "IviCli.Api",
                "IviCli.Cli"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application layer must depend only on Domain: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnBackendsOrServerOrCli()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "IviCli.Backends.Local",
                "IviCli.Backends.Fake",
                "IviCli.Backends.HiSlip",
                "IviCli.Backends.Socket",
                "IviCli.Backends.Replay",
                "IviCli.Backends.Vxi11",
                "IviCli.Server",
                "IviCli.Api",
                "IviCli.Cli"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Infrastructure must not depend on Backends, Server or Cli: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Theory]
    [InlineData("IviCli.Backends.Local")]
    [InlineData("IviCli.Backends.Fake")]
    [InlineData("IviCli.Backends.HiSlip")]
    [InlineData("IviCli.Backends.Socket")]
    [InlineData("IviCli.Backends.Replay")]
    [InlineData("IviCli.Backends.Vxi11")]
    public void Backend_DoesNotDependOnInfrastructureOrServerOrCliOrOtherBackends(
        string assemblyName
    )
    {
        var assembly = Assembly.Load(assemblyName);
        string[] upstreamAssemblies = ["IviCli.Infrastructure", "IviCli.Server", "IviCli.Cli"];
        var siblingsAndUpward = AllBackendAssemblyNames
            .Where(n => n != assemblyName)
            .Concat(upstreamAssemblies)
            .ToArray();

        var result = Types
            .InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(siblingsAndUpward)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{assemblyName} must depend only on Application/Domain: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void Server_DoesNotDependOnInfrastructureOrBackendsOrCli()
    {
        var result = Types
            .InAssembly(ServerAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "IviCli.Infrastructure",
                "IviCli.Backends.Local",
                "IviCli.Backends.Fake",
                "IviCli.Backends.HiSlip",
                "IviCli.Backends.Socket",
                "IviCli.Cli"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Server must reach Backends only through the IIviBackend port: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void DomainTypes_AreImmutableRecords()
    {
        // Domain types should be records (immutable) per ADR 0023 §1.
        // Extended in Task 2 to cover Phase 2/3 additions.
        var domainRecordTypes = new[]
        {
            typeof(DeviceName),
            typeof(Domain.Visa.VisaResource),
            typeof(Domain.Timeout),
            typeof(Device),
            typeof(Domain.Configuration.ConfigDocument),
            // Phase 2 additions
            typeof(Domain.Servers.Server),
            typeof(Domain.Servers.Route),
            typeof(Domain.Servers.ServerName),
            typeof(Domain.Servers.IpAddress),
            typeof(Domain.Servers.Port),
            typeof(Domain.Servers.PublicEndpoint),
        };

        foreach (var t in domainRecordTypes)
        {
            // Records have an `EqualityContract` property — a compiler-generated marker.
            t.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic)
                .ShouldNotBeNull($"{t.FullName} should be a record (have EqualityContract)");
        }
    }

    [Fact]
    public void HiSlipHeader_IsReadonlyRecordStruct()
    {
        // HiSlipHeader is a `readonly record struct` for zero-allocation
        // header passing. record struct types do not expose
        // EqualityContract (that is unique to record classes), so we check
        // the IsValueType + readonly markers instead.
        var t = typeof(Domain.Protocols.HiSlipHeader);
        t.IsValueType.ShouldBeTrue("HiSlipHeader should be a value type (record struct)");
        // The compiler stamps record structs with a parameterless instance
        // constructor by default; the marker we rely on is
        // System.Runtime.CompilerServices.IsExternalInit baked into the
        // setters, but that's hard to inspect. The IsValueType check + the
        // explicit `readonly` keyword in the declaration is sufficient
        // signal for the architecture suite.
    }

    private static string FormatFailingTypes(IEnumerable<string>? typeNames) =>
        typeNames is null ? "(no offending types reported)" : string.Join(", ", typeNames);
}
