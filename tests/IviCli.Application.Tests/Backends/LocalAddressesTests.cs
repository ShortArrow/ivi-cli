using System.Net.NetworkInformation;
using IviCli.Application.Backends;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

/// <summary>
/// A scanner skips a local address the system will not send from, such as
/// one another host on the network already uses.
/// </summary>
public sealed class LocalAddressesTests
{
    [Theory]
    [InlineData(DuplicateAddressDetectionState.Preferred)]
    [InlineData(DuplicateAddressDetectionState.Deprecated)]
    public void An_address_the_system_sends_from_is_usable(DuplicateAddressDetectionState state)
    {
        LocalAddresses.IsUsable(state).ShouldBeTrue();
    }

    [Theory]
    [InlineData(DuplicateAddressDetectionState.Duplicate)]
    [InlineData(DuplicateAddressDetectionState.Tentative)]
    [InlineData(DuplicateAddressDetectionState.Invalid)]
    public void An_address_the_system_will_not_send_from_is_not_usable(
        DuplicateAddressDetectionState state
    )
    {
        LocalAddresses.IsUsable(state).ShouldBeFalse();
    }

    [Fact]
    public void An_address_whose_state_the_platform_does_not_report_is_usable()
    {
        LocalAddresses.IsUsable(null).ShouldBeTrue();
    }
}
