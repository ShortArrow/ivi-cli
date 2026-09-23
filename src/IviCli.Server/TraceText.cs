namespace IviCli.Server;

/// <summary>
/// Shortens request and response text for a trace line. A response is the
/// instrument's data and a block transfer can run to megabytes, so a line
/// keeps the start of the text and says how long the whole was.
/// </summary>
internal static class TraceText
{
    /// <summary>Characters kept from the start of a long text.</summary>
    public const int Kept = 200;

    /// <summary>
    /// <paramref name="text"/> unchanged when it is at most <see cref="Kept"/>
    /// characters, otherwise its first <see cref="Kept"/> characters followed
    /// by the full length.
    /// </summary>
    public static string Clip(string text) =>
        text.Length <= Kept ? text : $"{text[..Kept]}… ({text.Length} chars)";
}
