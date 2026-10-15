using System.Text;
using IviCli.Backends.Local;
using IviCli.Domain.Devices;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// With a terminator set, VISA ends a read at its last character, and the
/// Local backend reads up to that character itself (ADR 0055 §5).
/// </summary>
public sealed class VisaTerminationTests
{
    [Theory]
    [InlineData("\n", (byte)'\n')]
    [InlineData("\r", (byte)'\r')]
    [InlineData("\r\n", (byte)'\n')]
    public void The_termination_character_is_the_last_of_the_terminator(
        string raw,
        byte expected
    ) => VisaTermination.Character(Termination.From(raw).ShouldBeOk()).ShouldBe(expected);

    [Theory]
    [InlineData("\n", "1.5\n", "1.5")]
    [InlineData("\r", "1.5\r", "1.5")]
    [InlineData("\r\n", "1.5\r\n", "1.5")]
    [InlineData("\r\n", "1.5\n", "1.5")]
    [InlineData("\n", "1.5\r\n", "1.5\r")]
    [InlineData("\r", "\r", "")]
    public void A_reply_loses_its_terminator(string terminator, string wire, string reply)
    {
        var chunks = new Queue<byte[]>([Encoding.Latin1.GetBytes(wire)]);

        var read = VisaTermination.ReadMessage(
            chunks.Dequeue,
            Termination.From(terminator).ShouldBeOk()
        );

        read.ShouldBe(reply);
    }

    /// <summary>A read can stop at a full buffer before the terminator arrives.</summary>
    [Fact]
    public void A_reply_split_over_several_reads_is_read_whole()
    {
        var chunks = new Queue<byte[]>([
            Encoding.Latin1.GetBytes("12"),
            Encoding.Latin1.GetBytes("34\r"),
            [(byte)'\n'],
        ]);

        VisaTermination.ReadMessage(chunks.Dequeue, Termination.CrLf).ShouldBe("1234");
    }

    /// <summary>A CR inside the reply is not the terminator's.</summary>
    [Fact]
    public void Only_a_CR_right_before_the_LF_is_removed()
    {
        var chunks = new Queue<byte[]>([Encoding.Latin1.GetBytes("a\rb\n")]);

        VisaTermination.ReadMessage(chunks.Dequeue, Termination.CrLf).ShouldBe("a\rb");
    }

    [Fact]
    public void Every_byte_passes_unchanged()
    {
        var chunks = new Queue<byte[]>([
            [0xB5, 0xFF, (byte)'\n'],
        ]);

        VisaTermination.ReadMessage(chunks.Dequeue, Termination.Lf).ShouldBe("\u00B5\u00FF");
    }

    /// <summary>A read that returns nothing will not bring the terminator either.</summary>
    [Fact]
    public void A_read_that_returns_nothing_fails()
    {
        var chunks = new Queue<byte[]>([Encoding.Latin1.GetBytes("12"), []]);

        Should.Throw<IOException>(() =>
            VisaTermination.ReadMessage(chunks.Dequeue, Termination.Lf)
        );
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void A_write_ends_with_the_terminator(string terminator) =>
        VisaTermination
            .Encode("VOLT 1.5\u00B5", Termination.From(terminator).ShouldBeOk())
            .ShouldBe(Encoding.Latin1.GetBytes("VOLT 1.5\u00B5" + terminator));
}
