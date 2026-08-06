using IviCli.Application.Logging;
using IviCli.Domain;
using Shouldly;
using Xunit;

namespace IviCli.Application.Tests.Logging;

/// <summary>
/// Pins the human-facing rendering of an <see cref="IviError"/>: the message
/// template's placeholders are substituted with the structured arguments, in
/// order, so console output never shows a raw <c>{Placeholder}</c>.
/// </summary>
public sealed class IviErrorMessagesTests
{
    [Fact]
    public void Substitutes_each_placeholder_with_its_argument_in_order()
    {
        var error = new TemplatedError(
            "waited {Seconds}s for {Device}",
            new object?[] { 3, "psu1" }
        );

        IviErrorMessages.Render(error).ShouldBe("waited 3s for psu1");
    }

    [Fact]
    public void Returns_a_template_without_placeholders_verbatim()
    {
        var error = new TemplatedError("transport failed", Array.Empty<object?>());

        IviErrorMessages.Render(error).ShouldBe("transport failed");
    }

    [Fact]
    public void Renders_a_null_argument_the_way_the_logging_stack_does()
    {
        var error = new TemplatedError("value was {Value}", new object?[] { null });

        IviErrorMessages.Render(error).ShouldBe("value was (null)");
    }

    [Fact]
    public void Leaves_placeholders_without_a_matching_argument_untouched()
    {
        var error = new TemplatedError("missing {A} and {B}", new object?[] { "only" });

        IviErrorMessages.Render(error).ShouldBe("missing only and {B}");
    }

    private sealed record TemplatedError(string Message, IReadOnlyList<object?> LogArgs) : IviError
    {
        public LogSeverity Severity => LogSeverity.Error;
    }
}
