using System.Collections.Immutable;
using IviCli.Application.Devices;
using IviCli.Cli.Commands;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using VerifyXunit;

namespace IviCli.Cli.Tests.Snapshots;

/// <summary>
/// Locks the <c>visa list</c> stdout contract with Verify snapshots
/// (ADR 0009 §7). The JSON shape declared by PRD §9 is the durable
/// contract for AI / CI consumers; the human form is a separate snapshot
/// so cosmetic tweaks are reviewed deliberately.
/// </summary>
public class DeviceListingFormatterSnapshotTests
{
    private static Device Dev(string name, string resource, int timeoutMs) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    private static DeviceListing EmptyListing() =>
        new(ImmutableArray<Device>.Empty, DefaultDevice: null);

    private static DeviceListing TwoDeviceListing()
    {
        var builder = ImmutableArray.CreateBuilder<Device>();
        builder.Add(Dev("psu1", "TCPIP0::192.168.0.10::inst0::INSTR", 3000));
        builder.Add(Dev("scope1", "USB0::0x0699::0x0408::C012345::INSTR", 5000));
        return new DeviceListing(
            builder.ToImmutable(),
            DefaultDevice: DeviceName.From("psu1").ShouldBeOk()
        );
    }

    [Fact]
    public Task FormatJson_Empty_MatchesSnapshot() =>
        Verifier
            .Verify(DeviceListingFormatter.FormatJson(EmptyListing()))
            .UseFileName("FormatJson_Empty");

    [Fact]
    public Task FormatJson_TwoDevicesWithDefault_MatchesSnapshot() =>
        Verifier
            .Verify(DeviceListingFormatter.FormatJson(TwoDeviceListing()))
            .UseFileName("FormatJson_TwoDevicesWithDefault");

    [Fact]
    public Task FormatHuman_Empty_MatchesSnapshot() =>
        Verifier
            .Verify(DeviceListingFormatter.FormatHuman(EmptyListing()))
            .UseFileName("FormatHuman_Empty");

    [Fact]
    public Task FormatHuman_TwoDevicesWithDefault_MatchesSnapshot() =>
        Verifier
            .Verify(DeviceListingFormatter.FormatHuman(TwoDeviceListing()))
            .UseFileName("FormatHuman_TwoDevicesWithDefault");
}
