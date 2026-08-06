using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

public class LocalUsbScannerTests
{
    private const string TekScope = "USB0::0x0699::0x0408::C012345::INSTR";
    private const string KeysightDmm = "USB0::0x2a8d::0x0101::MY1234::0::INSTR";

    [Fact]
    public async Task ScanAsync_returns_a_resource_per_discovered_usb_string()
    {
        // Given a finder that sees two USB instruments
        var finder = FakeVisaResourceFinder.Returning(TekScope, KeysightDmm);
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        var found = (await scanner.ScanAsync(ScanOptions.Default, default)).ShouldBeOk();

        // Then both are reported as USB resources with no *IDN? probe
        found.Length.ShouldBe(2);
        found.ShouldAllBe(r => r.Resource is VisaResource.Usb);
        found.ShouldAllBe(r => r.Idn == null);
        found.Select(r => r.Resource.ToCanonical()).ShouldBe([TekScope, KeysightDmm]);
    }

    [Fact]
    public async Task ScanAsync_queries_the_finder_with_the_usb_instr_pattern()
    {
        // Given a finder recording the patterns it is asked for
        var finder = FakeVisaResourceFinder.Returning(TekScope);
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        await scanner.ScanAsync(ScanOptions.Default, default);

        // Then only the USB INSTR pattern was requested
        finder.Patterns.ShouldBe(["USB?*::INSTR"]);
    }

    [Fact]
    public async Task ScanAsync_returns_empty_when_the_visa_runtime_is_missing()
    {
        // Given a machine with no VISA runtime installed
        var finder = FakeVisaResourceFinder.Failing(
            new LocalVisaRuntimeMissing("VISA runtime absent (test)")
        );
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        var result = await scanner.ScanAsync(ScanOptions.Default, default);

        // Then discovery contributes nothing rather than failing the scan
        result.ShouldBeOk().ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_returns_empty_when_the_finder_fails_for_any_other_reason()
    {
        // Given a finder whose reflective call blew up
        var finder = FakeVisaResourceFinder.Failing(
            new LocalVisaIoFailure("Find threw (test)", new InvalidOperationException("boom"))
        );
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        var result = await scanner.ScanAsync(ScanOptions.Default, default);

        // Then the failure never reaches the aggregate scan result
        result.ShouldBeOk().ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_skips_strings_that_do_not_parse()
    {
        // Given a finder returning one unparseable entry among valid ones
        var finder = FakeVisaResourceFinder.Returning(TekScope, "not-a-resource", KeysightDmm);
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        var found = (await scanner.ScanAsync(ScanOptions.Default, default)).ShouldBeOk();

        // Then the valid entries are still reported
        found.Select(r => r.Resource.ToCanonical()).ShouldBe([TekScope, KeysightDmm]);
    }

    [Fact]
    public async Task ScanAsync_skips_resources_that_are_not_usb()
    {
        // Given a finder that also returned a LAN resource
        var finder = FakeVisaResourceFinder.Returning(
            "TCPIP0::192.168.0.10::inst0::INSTR",
            TekScope
        );
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        var found = (await scanner.ScanAsync(ScanOptions.Default, default)).ShouldBeOk();

        // Then only the USB resource is reported
        found.Select(r => r.Resource.ToCanonical()).ShouldBe([TekScope]);
    }

    [Fact]
    public async Task ScanAsync_returns_empty_when_the_finder_sees_nothing()
    {
        // Given a runtime that is installed but sees no USB instrument
        var finder = FakeVisaResourceFinder.Returning();
        var scanner = new LocalUsbScanner(finder);

        // When the scanner runs
        var result = await scanner.ScanAsync(ScanOptions.Default, default);

        // Then the scan succeeds with nothing to report
        result.ShouldBeOk().ShouldBeEmpty();
    }

    [Fact]
    public void AddIviCliLocalUsbScanner_registers_the_scanner_and_its_finder()
    {
        // Given an empty container
        var services = new ServiceCollection();

        // When the Local USB scanner is registered
        services.AddIviCliLocalUsbScanner();

        // Then the VISA.NET finder backs a LocalUsbScanner exposed as IBackendScanner
        services
            .Single(d => d.ServiceType == typeof(IVisaResourceFinder))
            .ImplementationType.ShouldBe(typeof(VisaResourceFinder));
        services.ShouldContain(d => d.ServiceType == typeof(LocalUsbScanner));
        services.ShouldContain(d => d.ServiceType == typeof(IBackendScanner));
    }
}

internal sealed class FakeVisaResourceFinder : IVisaResourceFinder
{
    private readonly Result<ImmutableArray<string>, LocalVisaError> _result;

    private FakeVisaResourceFinder(Result<ImmutableArray<string>, LocalVisaError> result) =>
        _result = result;

    public List<string> Patterns { get; } = new();

    public static FakeVisaResourceFinder Returning(params string[] resources) =>
        new(
            Result.Success<ImmutableArray<string>, LocalVisaError>(ImmutableArray.Create(resources))
        );

    public static FakeVisaResourceFinder Failing(LocalVisaError error) =>
        new(Result.Failure<ImmutableArray<string>, LocalVisaError>(error));

    public Result<ImmutableArray<string>, LocalVisaError> Find(string pattern)
    {
        Patterns.Add(pattern);
        return _result;
    }
}
