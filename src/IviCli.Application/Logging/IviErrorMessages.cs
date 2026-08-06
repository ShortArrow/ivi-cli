using System.Text.RegularExpressions;
using IviCli.Domain;

namespace IviCli.Application.Logging;

/// <summary>
/// Renders an <see cref="IviError"/> for direct human-facing output
/// (console text, not a log line): the message template's placeholders are
/// substituted with the structured arguments, in order.
/// </summary>
public static partial class IviErrorMessages
{
    /// <summary>Returns the error's message with its arguments substituted.</summary>
    public static string Render(IviError error)
    {
        var index = 0;
        return Placeholder()
            .Replace(
                error.Message,
                match =>
                    index < error.LogArgs.Count
                        ? error.LogArgs[index++]?.ToString() ?? "(null)"
                        : match.Value
            );
    }

    [GeneratedRegex("\\{[^{}]+\\}")]
    private static partial Regex Placeholder();
}
