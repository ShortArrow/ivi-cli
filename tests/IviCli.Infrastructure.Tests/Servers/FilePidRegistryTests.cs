using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;
using IviCli.Infrastructure.Servers;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Servers;

public class FilePidRegistryTests
{
    private const string Dir = "/var/lib/ivi-cli/servers";

    private static ServerName Name(string s) => ServerName.From(s).ShouldBeOk();

    [Fact]
    public async Task WriteAsync_then_ReadAsync_round_trips_entry()
    {
        var fs = new MockFileSystem();
        var registry = new FilePidRegistry(fs, Dir);
        var startedAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

        var write = await registry.WriteAsync(Name("sock1"), 4242, startedAt, default);
        write.ShouldBeOk();

        var read = await registry.ReadAsync(Name("sock1"), default);
        var entry = read.ShouldBeOk();
        entry.ShouldNotBeNull();
        entry!.ProcessId.ShouldBe(4242);
        entry.StartedAt.ShouldBe(startedAt);
    }

    [Fact]
    public async Task ReadAsync_returns_null_when_no_pid_file()
    {
        var fs = new MockFileSystem();
        var registry = new FilePidRegistry(fs, Dir);
        var read = await registry.ReadAsync(Name("missing"), default);
        read.ShouldBeOk().ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_pid_file()
    {
        var fs = new MockFileSystem();
        var registry = new FilePidRegistry(fs, Dir);
        await registry.WriteAsync(Name("sock1"), 1, DateTimeOffset.UtcNow, default);

        var delete = await registry.DeleteAsync(Name("sock1"), default);
        delete.ShouldBeOk();

        var read = await registry.ReadAsync(Name("sock1"), default);
        read.ShouldBeOk().ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_is_idempotent_when_file_missing()
    {
        var fs = new MockFileSystem();
        var registry = new FilePidRegistry(fs, Dir);
        var delete = await registry.DeleteAsync(Name("sock1"), default);
        delete.ShouldBeOk();
    }

    [Fact]
    public async Task WriteAsync_creates_directory_on_first_call()
    {
        var fs = new MockFileSystem();
        var registry = new FilePidRegistry(fs, Dir);
        await registry.WriteAsync(Name("sock1"), 1, DateTimeOffset.UtcNow, default);
        fs.Directory.Exists(Dir).ShouldBeTrue();
    }

    [Fact]
    public async Task ListAsync_returns_every_entry_sorted_by_name()
    {
        var fs = new MockFileSystem();
        var registry = new FilePidRegistry(fs, Dir);
        await registry.WriteAsync(Name("zeta"), 1, DateTimeOffset.UtcNow, default);
        await registry.WriteAsync(Name("alpha"), 2, DateTimeOffset.UtcNow, default);

        var list = await registry.ListAsync(default);
        var entries = list.ShouldBeOk();
        entries.Length.ShouldBe(2);
        entries[0].Name.Value.ShouldBe("alpha");
        entries[1].Name.Value.ShouldBe("zeta");
    }

    [Fact]
    public async Task ReadAsync_reports_corruption_when_payload_is_malformed()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Dir);
        fs.AddFile("/var/lib/ivi-cli/servers/bad.pid", new MockFileData("not a pid"));
        var registry = new FilePidRegistry(fs, Dir);

        var read = await registry.ReadAsync(Name("bad"), default);
        read.ShouldBeOfType<Result<ServerProcessEntry?, ServerProcessRegistryError>.Error>();
    }
}
