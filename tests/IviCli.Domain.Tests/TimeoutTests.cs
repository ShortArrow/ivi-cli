using IviCli.Domain;
using IviCli.TestKit;

namespace IviCli.Domain.Tests;

public class TimeoutTests
{
    [Fact]
    public void From_WithPositiveDuration_ReturnsOk()
    {
        // Given / When
        var result = Timeout.From(TimeSpan.FromSeconds(3));

        // Then
        result.ShouldBeOk().Value.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void From_WithZeroDuration_ReturnsOk()
    {
        // Given / When
        var result = Timeout.From(TimeSpan.Zero);

        // Then
        result.ShouldBeOk().Value.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void From_WithMaxDuration_ReturnsOk()
    {
        // Given / When
        var result = Timeout.From(Timeout.Maximum);

        // Then
        result.ShouldBeOk().Value.ShouldBe(Timeout.Maximum);
    }

    [Fact]
    public void From_WithNegativeDuration_ReturnsInvalidTimeoutValue()
    {
        // Given
        var raw = TimeSpan.FromMilliseconds(-1);

        // When
        var result = Timeout.From(raw);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<InvalidTimeoutValue>();
        err.Raw.ShouldBe(raw);
    }

    [Fact]
    public void From_WithDurationAboveMaximum_ReturnsInvalidTimeoutValue()
    {
        // Given
        var raw = Timeout.Maximum + TimeSpan.FromTicks(1);

        // When
        var result = Timeout.From(raw);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<InvalidTimeoutValue>();
        err.Raw.ShouldBe(raw);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3000)]
    [InlineData(60_000)]
    public void FromMilliseconds_WithValidValue_ReturnsOk(int ms)
    {
        // Given / When
        var result = Timeout.FromMilliseconds(ms);

        // Then
        result.ShouldBeOk().Value.ShouldBe(TimeSpan.FromMilliseconds(ms));
    }

    [Fact]
    public void FromMilliseconds_WithNegativeValue_ReturnsInvalidTimeoutValue()
    {
        // Given / When
        var result = Timeout.FromMilliseconds(-100);

        // Then
        result.ShouldBeError().ShouldBeOfType<InvalidTimeoutValue>();
    }

    [Fact]
    public void Milliseconds_ReflectsValueInMilliseconds()
    {
        // Given
        var t = Timeout.FromMilliseconds(2500).ShouldBeOk();

        // When / Then
        t.Milliseconds.ShouldBe(2500);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        // Given
        var a = Timeout.FromMilliseconds(1000).ShouldBeOk();
        var b = Timeout.FromMilliseconds(1000).ShouldBeOk();
        var c = Timeout.FromMilliseconds(2000).ShouldBeOk();

        // When / Then
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
