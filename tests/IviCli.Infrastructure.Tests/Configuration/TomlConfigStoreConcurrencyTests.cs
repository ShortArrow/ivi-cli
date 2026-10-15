using System.IO.Abstractions;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Infrastructure.Configuration;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Configuration;

/// <summary>
/// A load that overlaps a save, by this process or another, sees either the
/// old document or the new one, never no file: a missing file loads as an
/// empty configuration, which would send a registered ASRL board to VISA
/// (ADR 0055 §3).
/// </summary>
public sealed class TomlConfigStoreConcurrencyTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateTempSubdirectory("ivicli-config-")
        .FullName;

    [Fact(Timeout = 20000)]
    public async Task A_load_during_saves_always_sees_a_whole_document()
    {
        var path = Path.Combine(_directory, "config.toml");
        var fs = new FileSystem();
        var document = ConfigDocument
            .Empty.AddAsrlBoard(AsrlBoardRegistration.From(1, "COM100").ShouldBeOk())
            .ShouldBeOk();
        var writer = new TomlConfigStore(fs, path);
        var reader = new TomlConfigStore(fs, path);
        (await writer.SaveAsync(document, TestContext.Current.CancellationToken)).ShouldBeOk();

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var saveFailures = 0;
        var saving = Task.Run(
            async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    if (
                        await writer.SaveAsync(document, CancellationToken.None)
                        is not Result<Unit, ConfigStoreError>.Ok
                    )
                    {
                        saveFailures++;
                    }
                }
            },
            TestContext.Current.CancellationToken
        );

        var loads = 0;
        var wrong = 0;
        while (!stop.IsCancellationRequested)
        {
            loads++;
            var loaded = await reader.LoadAsync(TestContext.Current.CancellationToken);
            if (
                loaded
                is not Result<ConfigDocument, ConfigStoreError>.Ok { Value.AsrlBoards.Length: 1 }
            )
            {
                wrong++;
            }
        }
        await saving;

        loads.ShouldBeGreaterThan(100);
        wrong.ShouldBe(0, $"{wrong} of {loads} loads saw no whole document");
        saveFailures.ShouldBe(0);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
