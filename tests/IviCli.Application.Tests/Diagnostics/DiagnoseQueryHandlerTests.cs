using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Backends;
using IviCli.Application.Diagnostics;
using IviCli.Backends.Fake;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Diagnostics;

public class DiagnoseQueryHandlerTests
{
    private static DiagnoseQueryHandler MakeHandler(
        bool configExists,
        bool logDirExists,
        IEnumerable<IIviBackend>? backends = null,
        IEnumerable<IBackendScanner>? scanners = null
    )
    {
        var fs = new MockFileSystem();
        const string configPath = "/etc/ivi-cli/config.toml";
        const string logDir = "/var/log/ivi-cli";

        if (configExists)
        {
            fs.AddFile(configPath, new MockFileData(""));
        }
        if (logDirExists)
        {
            fs.AddDirectory(logDir);
        }

        return new DiagnoseQueryHandler(
            fs,
            backends ?? Array.Empty<IIviBackend>(),
            scanners ?? Array.Empty<IBackendScanner>(),
            new DiagnoseHandlerOptions(configPath, logDir)
        );
    }

    [Fact]
    public async Task Handle_AllPresent_ReportsAllOk()
    {
        // Given
        var handler = MakeHandler(
            configExists: true,
            logDirExists: true,
            backends: new IIviBackend[] { new FakeBackend() },
            scanners: new IBackendScanner[] { new FakeBackendScanner() }
        );

        // When
        var result = await handler.HandleAsync(new DiagnoseQuery(), CancellationToken.None);

        // Then
        var report = result.ShouldBeOk();
        report.Checks.ShouldNotBeEmpty();
        report.Checks.ShouldAllBe(c => c.Status == DiagnosticStatus.Ok);
    }

    [Fact]
    public async Task Handle_NoBackends_ReportsError()
    {
        // Given
        var handler = MakeHandler(configExists: true, logDirExists: true);

        // When
        var result = await handler.HandleAsync(new DiagnoseQuery(), CancellationToken.None);

        // Then
        var report = result.ShouldBeOk();
        report.Checks.ShouldContain(c =>
            c.Name == "backends" && c.Status == DiagnosticStatus.Error
        );
    }

    [Fact]
    public async Task Handle_MissingConfig_ReportsWarning()
    {
        // Given
        var handler = MakeHandler(
            configExists: false,
            logDirExists: true,
            backends: new IIviBackend[] { new FakeBackend() }
        );

        // When
        var result = await handler.HandleAsync(new DiagnoseQuery(), CancellationToken.None);

        // Then
        var report = result.ShouldBeOk();
        report.Checks.ShouldContain(c =>
            c.Name == "config" && c.Status == DiagnosticStatus.Warning
        );
    }
}
