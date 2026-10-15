using System.IO.Abstractions.TestingHelpers;
using IviCli.Infrastructure.Devices;

namespace IviCli.Infrastructure.Tests.Devices;

/// <summary>
/// Two names of one port resolve alike, and a port is present when the
/// operating system lists it (ADR 0055 §3).
/// </summary>
public sealed class SystemSerialPortsTests
{
    [Fact]
    public void On_Unix_a_link_resolves_like_the_port_it_points_at()
    {
        // Given
        var fs = new MockFileSystem();
        var target = fs.Path.GetFullPath("/dev/ttyUSB0");
        var link = fs.Path.GetFullPath("/dev/serial/by-id/usb-FTDI");
        fs.AddFile(target, new MockFileData(""));
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(link)!);
        fs.File.CreateSymbolicLink(link, target);
        var ports = new SystemSerialPorts(fs, windows: false, windowsPortNames: () => []);

        // When / Then
        ports.Resolve(link).ShouldBe(ports.Resolve(target));
        ports.Resolve(fs.Path.GetFullPath("/dev/ttyUSB1")).ShouldNotBe(ports.Resolve(target));
    }

    [Fact]
    public void On_Unix_a_port_is_present_while_its_file_exists()
    {
        // Given
        var fs = new MockFileSystem();
        var present = fs.Path.GetFullPath("/dev/ttyUSB0");
        fs.AddFile(present, new MockFileData(""));
        var ports = new SystemSerialPorts(fs, windows: false, windowsPortNames: () => []);

        // When / Then
        ports.Exists(present).ShouldBeTrue();
        ports.Exists(fs.Path.GetFullPath("/dev/ttyUSB1")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("com3")]
    [InlineData("COM3")]
    [InlineData(@"\\.\COM3")]
    public void On_Windows_names_of_one_COM_port_resolve_alike(string name)
    {
        // Given
        var ports = new SystemSerialPorts(new MockFileSystem(), windows: true, () => []);

        // When / Then
        ports.Resolve(name).ShouldBe(ports.Resolve("COM3"));
    }

    [Fact]
    public void On_Windows_a_port_is_present_while_the_system_lists_it()
    {
        // Given
        var ports = new SystemSerialPorts(new MockFileSystem(), windows: true, () => ["COM3"]);

        // When / Then
        ports.Exists("com3").ShouldBeTrue();
        ports.Exists(@"\\.\COM3").ShouldBeTrue();
        ports.Exists("COM4").ShouldBeFalse();
    }
}
