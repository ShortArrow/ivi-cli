using System.Text;
using System.Text.Json;
using IviCli.Api.WebSockets;
using Shouldly;

namespace IviCli.Api.Tests.WebSockets;

public sealed class VisaWebSocketCodecTests
{
    [Fact]
    public void Decode_query_frame_yields_Query_request()
    {
        var req = VisaWebSocketCodec.Decode("{\"op\":\"query\",\"scpi\":\"*IDN?\"}");
        var q = req.ShouldBeOfType<VisaWebSocketRequest.Query>();
        q.Scpi.ShouldBe("*IDN?");
    }

    [Fact]
    public void Decode_write_frame_yields_Write_request()
    {
        var req = VisaWebSocketCodec.Decode("{\"op\":\"write\",\"scpi\":\"OUTP ON\"}");
        var w = req.ShouldBeOfType<VisaWebSocketRequest.Write>();
        w.Scpi.ShouldBe("OUTP ON");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"op\":\"unknown\",\"scpi\":\"*IDN?\"}")]
    [InlineData("{\"scpi\":\"*IDN?\"}")]
    [InlineData("{\"op\":\"query\"}")]
    [InlineData("{\"op\":\"query\",\"scpi\":\"\"}")]
    [InlineData("{\"op\":\"query\",\"scpi\":\"  \"}")]
    public void Decode_returns_null_for_malformed_or_unknown_frames(string frame)
    {
        VisaWebSocketCodec.Decode(frame).ShouldBeNull();
    }

    [Fact]
    public void EncodeFrame_Response_round_trips_with_latency()
    {
        var bytes = VisaWebSocketCodec.EncodeFrame(
            new VisaWebSocketEvent.Response("*IDN?", "ACME,PSU,1,1.0", 12)
        );
        var text = Encoding.UTF8.GetString(bytes);
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("event").GetString().ShouldBe("response");
        doc.RootElement.GetProperty("scpi").GetString().ShouldBe("*IDN?");
        doc.RootElement.GetProperty("response").GetString().ShouldBe("ACME,PSU,1,1.0");
        doc.RootElement.GetProperty("latencyMs").GetInt32().ShouldBe(12);
    }

    [Fact]
    public void EncodeFrame_Ack_has_event_and_scpi()
    {
        var bytes = VisaWebSocketCodec.EncodeFrame(new VisaWebSocketEvent.Ack("OUTP ON"));
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        doc.RootElement.GetProperty("event").GetString().ShouldBe("ack");
        doc.RootElement.GetProperty("scpi").GetString().ShouldBe("OUTP ON");
    }

    [Fact]
    public void EncodeFrame_Error_has_code_and_message()
    {
        var bytes = VisaWebSocketCodec.EncodeFrame(
            new VisaWebSocketEvent.Error(VisaWebSocketErrorCodes.BackendFailure, "boom")
        );
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        doc.RootElement.GetProperty("event").GetString().ShouldBe("error");
        doc.RootElement.GetProperty("code").GetString().ShouldBe("backend_failure");
        doc.RootElement.GetProperty("message").GetString().ShouldBe("boom");
    }

    [Fact]
    public void EncodeFrame_response_preserves_non_ascii_payload()
    {
        var bytes = VisaWebSocketCodec.EncodeFrame(
            new VisaWebSocketEvent.Response("*IDN?", "メーカー,モデル,001,1.0", 3)
        );
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        doc.RootElement.GetProperty("response").GetString().ShouldBe("メーカー,モデル,001,1.0");
    }
}
