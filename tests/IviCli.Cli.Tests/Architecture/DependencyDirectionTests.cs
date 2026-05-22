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
                "IviCli.Cli"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain layer must not depend on any other layer: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrBackendsOrCli()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "IviCli.Infrastructure",
                "IviCli.Backends.Local",
                "IviCli.Backends.Fake",
                "IviCli.Cli"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application layer must depend only on Domain: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnBackendsOrCli()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny("IviCli.Backends.Local", "IviCli.Backends.Fake", "IviCli.Cli")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Infrastructure must not depend on Backends or Cli: "
                + FormatFailingTypes(result.FailingTypeNames)
        );
    }

    [Fact]
    public void Backends_DoNotDependOnInfrastructureOrCliOrEachOther()
    {
        var localResult = Types
            .InAssembly(BackendsLocalAssembly)
            .Should()
            .NotHaveDependencyOnAny("IviCli.Infrastructure", "IviCli.Backends.Fake", "IviCli.Cli")
            .GetResult();
        localResult.IsSuccessful.ShouldBeTrue(
            "Backends.Local must depend only on Application/Domain: "
                + FormatFailingTypes(localResult.FailingTypeNames)
        );

        var fakeResult = Types
            .InAssembly(BackendsFakeAssembly)
            .Should()
            .NotHaveDependencyOnAny("IviCli.Infrastructure", "IviCli.Backends.Local", "IviCli.Cli")
            .GetResult();
        fakeResult.IsSuccessful.ShouldBeTrue(
            "Backends.Fake must depend only on Application/Domain: "
                + FormatFailingTypes(fakeResult.FailingTypeNames)
        );
    }

    [Fact]
    public void DomainTypes_AreImmutableRecords()
    {
        // Domain types should be records (immutable) per ADR 0023 §1.
        // This is a smoke test that hits a few representative types.
        var domainRecordTypes = new[]
        {
            typeof(DeviceName),
            typeof(Domain.Visa.VisaResource),
            typeof(Domain.Timeout),
            typeof(Device),
            typeof(Domain.Configuration.ConfigDocument),
        };

        foreach (var t in domainRecordTypes)
        {
            // Records have an `EqualityContract` property — a compiler-generated marker.
            t.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic)
                .ShouldNotBeNull($"{t.FullName} should be a record (have EqualityContract)");
        }
    }

    private static string FormatFailingTypes(IEnumerable<string>? typeNames) =>
        typeNames is null ? "(no offending types reported)" : string.Join(", ", typeNames);
}
