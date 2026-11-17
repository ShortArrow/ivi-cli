using System.Globalization;
using System.Text;

namespace IviCli.Infrastructure;

/// <summary>
/// Writes a value as a TOML basic string, for every TOML file this assembly
/// writes by hand.
/// </summary>
internal static class TomlBasicString
{
    /// <summary>
    /// A TOML basic string holding <paramref name="value"/>, with backslashes,
    /// quotes and control characters escaped, so a TOML parser reads back
    /// exactly <paramref name="value"/>.
    /// </summary>
    public static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            builder.Append(
                c switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    < ' ' or '\u007f' => string.Create(
                        CultureInfo.InvariantCulture,
                        $"\\u{(int)c:X4}"
                    ),
                    _ => c.ToString(),
                }
            );
        }
        return builder.Append('"').ToString();
    }
}
