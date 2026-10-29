using IviCli.Application.Scripting;
using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class ScriptParseMessageTests
{
    [Fact]
    public void A_script_that_cannot_be_read_is_reported_by_its_line_and_reason()
    {
        // Given
        var error = new ScpiScriptInvalidDirective(
            3,
            "!sleep -1",
            "sleep argument must be a non-negative integer"
        );

        // When
        var message = ScriptParseMessage.For(error);

        // Then
        message.ShouldBe("error: script line 3: sleep argument must be a non-negative integer.");
    }
}
