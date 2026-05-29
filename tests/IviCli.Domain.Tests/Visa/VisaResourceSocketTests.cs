using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Visa;

/// <summary>
/// Locks in the SOCKET-form TCPIP resource (Batch X) used by raw-
/// socket SCPI listeners and by ivi-cli's own SOCKET gateway in
/// the mock-VISA container (ADR 0018 §3).
/// </summary>
public sealed class VisaResourceSocketTests
{
    [Theory]
    [InlineData("TCPIP0::192.168.0.10::5025::SOCKET", 0, "192.168.0.10", 5025)]
    [InlineData("TCPIP::psu-lab.local::5025::SOCKET", 0, "psu-lab.local", 5025)]
    [InlineData("TCPIP3::10.0.0.5::4242::SOCKET", 3, "10.0.0.5", 4242)]
    [InlineData("TCPIP0::127.0.0.1::1::SOCKET", 0, "127.0.0.1", 1)]
    [InlineData("TCPIP0::127.0.0.1::65535::SOCKET", 0, "127.0.0.1", 65535)]
    public void Parse_SOCKET_form_returns_TcpipSocket(
        string raw,
        int expectedBoard,
        string expectedHost,
        int expectedPort
    )
    {
        var result = VisaResource.Parse(raw);

        var sock = result.ShouldBeOk().ShouldBeOfType<VisaResource.TcpipSocket>();
        sock.Board.ShouldBe(expectedBoard);
        sock.Host.ShouldBe(expectedHost);
        sock.Port.ShouldBe(expectedPort);
    }

    [Theory]
    [InlineData("TCPIP0::host::SOCKET")] // missing port segment (3-seg SOCKET ambiguous)
    [InlineData("TCPIP0::host::0::SOCKET")] // port 0 not allowed
    [InlineData("TCPIP0::host::65536::SOCKET")] // port out of range
    [InlineData("TCPIP0::host::-1::SOCKET")] // negative port
    [InlineData("TCPIP0::host::abc::SOCKET")] // non-numeric port
    [InlineData("TCPIP0::host:: ::SOCKET")] // whitespace port
    [InlineData("TCPIP0::::5025::SOCKET")] // empty host
    public void Parse_rejects_malformed_SOCKET_input(string raw)
    {
        VisaResource.Parse(raw).ShouldBeError();
    }

    [Fact]
    public void TcpipSocket_log_string_masks_host_only()
    {
        var resource = VisaResource.Parse("TCPIP0::192.168.0.10::5025::SOCKET").ShouldBeOk();
        resource.ToLogString().ShouldBe("TCPIP0::***::5025::SOCKET");
    }

    [Fact]
    public void Tcpip_INSTR_form_still_returns_Tcpip_not_TcpipSocket()
    {
        // Sanity check: SOCKET parsing must not regress INSTR handling.
        var resource = VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk();
        resource.ShouldBeOfType<VisaResource.Tcpip>();
    }

    [Fact]
    public void Equality_distinguishes_TcpipSocket_from_Tcpip_with_same_board_host()
    {
        var sock = VisaResource.Parse("TCPIP0::host::5025::SOCKET").ShouldBeOk();
        var instr = VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk();
        sock.ShouldNotBe(instr);
    }
}
