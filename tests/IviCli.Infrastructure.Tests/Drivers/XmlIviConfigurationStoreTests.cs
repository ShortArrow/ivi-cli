using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Drivers;
using IviCli.Domain;
using IviCli.Infrastructure.Drivers;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Drivers;

/// <summary>
/// Verifies that <see cref="XmlIviConfigurationStore"/> handles the
/// real IVI Configuration Store XML shape — including the namespace-
/// prefix variants seen across IVI Shared Components versions —
/// without depending on an installed IVI runtime.
/// </summary>
public sealed class XmlIviConfigurationStoreTests
{
    private const string StorePath = "/var/ivi/IviConfigurationStore.xml";

    [Fact]
    public async Task ListDriversAsync_parses_SoftwareModule_entries()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <IviConfigStore>
                <SoftwareModule>
                    <Name>IviScope</Name>
                    <Description>IVI Scope class driver</Description>
                    <ModulePath>C:\Program Files\IVI Foundation\IVI\Bin\IviScope.dll</ModulePath>
                    <Prefix>IviScope</Prefix>
                </SoftwareModule>
                <SoftwareModule>
                    <Name>Ag344xx</Name>
                    <ModulePath>C:\Drivers\Ag344xx.dll</ModulePath>
                </SoftwareModule>
            </IviConfigStore>
            """;
        var sut = MakeSut(xml);

        var result = await sut.ListDriversAsync(default);

        var drivers = result.ShouldBeOk();
        drivers.Length.ShouldBe(2);

        drivers[0].Name.ShouldBe("IviScope");
        drivers[0].Description.ShouldBe("IVI Scope class driver");
        drivers[0].ModulePath.ShouldBe(@"C:\Program Files\IVI Foundation\IVI\Bin\IviScope.dll");
        drivers[0].Prefix.ShouldBe("IviScope");

        drivers[1].Name.ShouldBe("Ag344xx");
        drivers[1].Description.ShouldBeNull();
        drivers[1].ModulePath.ShouldBe(@"C:\Drivers\Ag344xx.dll");
        drivers[1].Prefix.ShouldBeNull();
    }

    [Fact]
    public async Task ListLogicalNamesAsync_parses_LogicalName_entries()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <IviConfigStore>
                <LogicalName>
                    <Name>MyScope</Name>
                    <Description>Bench oscilloscope</Description>
                    <Session>MyScopeSession</Session>
                </LogicalName>
                <LogicalName>
                    <Name>UnboundLogical</Name>
                </LogicalName>
            </IviConfigStore>
            """;
        var sut = MakeSut(xml);

        var result = await sut.ListLogicalNamesAsync(default);

        var names = result.ShouldBeOk();
        names.Length.ShouldBe(2);

        names[0].Name.ShouldBe("MyScope");
        names[0].Description.ShouldBe("Bench oscilloscope");
        names[0].DriverSessionName.ShouldBe("MyScopeSession");

        names[1].Name.ShouldBe("UnboundLogical");
        names[1].Description.ShouldBeNull();
        names[1].DriverSessionName.ShouldBeNull();
    }

    [Fact]
    public async Task Missing_store_file_returns_NotFound_not_exception()
    {
        var fs = new MockFileSystem();
        var sut = new XmlIviConfigurationStore(fs, StorePath);

        var driverResult = await sut.ListDriversAsync(default);
        driverResult.ShouldBeError().ShouldBeOfType<IviConfigurationStoreNotFound>();

        var logicalResult = await sut.ListLogicalNamesAsync(default);
        logicalResult.ShouldBeError().ShouldBeOfType<IviConfigurationStoreNotFound>();
    }

    [Fact]
    public async Task Malformed_XML_returns_ParseFailure()
    {
        var sut = MakeSut("<IviConfigStore><Unclosed>");

        var result = await sut.ListDriversAsync(default);

        result.ShouldBeError().ShouldBeOfType<IviConfigurationStoreParseFailure>();
    }

    [Fact]
    public async Task Namespaced_XML_still_parses_via_local_name_match()
    {
        // Real IVI store files use an xmlns="http://www.ivifoundation.org/..."
        // namespace; the parser must match by local name.
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ivi:IviConfigStore xmlns:ivi="http://www.ivifoundation.org/configurationstore">
                <ivi:SoftwareModule>
                    <ivi:Name>NamespacedDriver</ivi:Name>
                </ivi:SoftwareModule>
            </ivi:IviConfigStore>
            """;
        var sut = MakeSut(xml);

        var result = await sut.ListDriversAsync(default);

        result.ShouldBeOk().Single().Name.ShouldBe("NamespacedDriver");
    }

    [Fact]
    public async Task SoftwareModule_without_Name_is_silently_skipped()
    {
        var xml = """
            <IviConfigStore>
                <SoftwareModule>
                    <Description>nameless</Description>
                </SoftwareModule>
                <SoftwareModule>
                    <Name>ValidDriver</Name>
                </SoftwareModule>
            </IviConfigStore>
            """;
        var sut = MakeSut(xml);

        var result = await sut.ListDriversAsync(default);

        result.ShouldBeOk().Single().Name.ShouldBe("ValidDriver");
    }

    private static XmlIviConfigurationStore MakeSut(string xml)
    {
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData> { [StorePath] = new(xml) }
        );
        return new XmlIviConfigurationStore(fs, StorePath);
    }
}
