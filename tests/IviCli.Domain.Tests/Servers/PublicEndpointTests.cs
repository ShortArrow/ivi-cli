using IviCli.Domain.Servers;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Servers;

/// <summary>
/// The endpoint name is what a USB/IP route publishes as its
/// <c>busid</c> (ADR 0049 §1), and a busid is written <c>bus-port</c> —
/// <c>1-1</c>, <c>2-3</c>. The format rule predates that use, so the
/// shape is pinned here: tightening the pattern later would silently
/// make every exported device unaddressable.
/// </summary>
public sealed class PublicEndpointTests
{
    [Theory]
    [InlineData("1-1")]
    [InlineData("2-3")]
    [InlineData("1-1-4")]
    public void A_usbip_busid_is_a_valid_public_endpoint(string busId)
    {
        PublicEndpoint.From(busId).ShouldBeOk().Value.ShouldBe(busId);
    }

    [Fact]
    public void An_endpoint_may_not_open_with_the_separator()
    {
        PublicEndpoint.From("-1").ShouldBeError();
    }
}
