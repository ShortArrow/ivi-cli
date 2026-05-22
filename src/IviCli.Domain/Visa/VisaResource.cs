using System.Globalization;
using System.Text.RegularExpressions;

namespace IviCli.Domain.Visa;

/// <summary>
/// A parsed VISA resource — the structured form of strings such as
/// <c>TCPIP0::192.168.0.10::inst0::INSTR</c>.
/// </summary>
/// <remarks>
/// Variants are added as new transports are supported. Cycle 1 of the
/// implementation introduces <see cref="Tcpip"/>; USB, GPIB, and SOCKET
/// follow in later cycles.
/// </remarks>
public abstract partial record VisaResource
{
    private VisaResource() { }

    /// <summary>
    /// A TCPIP LAN-attached instrument resource of the form
    /// <c>TCPIP[board]::host::lan_device::INSTR</c>.
    /// </summary>
    /// <param name="Board">The TCPIP interface number (defaults to <c>0</c>).</param>
    /// <param name="Host">The IPv4/IPv6 literal or hostname of the instrument.</param>
    /// <param name="LanDevice">
    /// The LAN device name (typically <c>inst0</c> for VXI-11 / LXI or
    /// <c>hislipN</c> for HiSLIP). Defaults to <c>inst0</c> when omitted in input.
    /// </param>
    public sealed record Tcpip(int Board, string Host, string LanDevice) : VisaResource;

    [GeneratedRegex("^TCPIP(?<board>[0-9]*)$")]
    private static partial Regex TcpipPrefix();

    private const string SegmentSeparator = "::";
    private const string InstrSuffix = "INSTR";

    /// <summary>
    /// Parses a VISA resource string into its structured representation.
    /// </summary>
    /// <param name="raw">The candidate VISA resource string.</param>
    /// <returns>
    /// <see cref="Result{T, TError}.Ok"/> with the parsed resource on success;
    /// otherwise <see cref="Result{T, TError}.Error"/> wrapping
    /// <see cref="InvalidVisaResourceFormat"/>.
    /// </returns>
    public static Result<VisaResource, VisaResourceError> Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Fail(raw);
        }

        var segments = raw.Split(SegmentSeparator);

        // Forms accepted in cycle 1:
        //   TCPIP[N]::host::lan_device::INSTR  -> 4 segments
        //   TCPIP[N]::host::INSTR              -> 3 segments (lan_device defaults to inst0)
        if (segments.Length is < 3 or > 4)
        {
            return Fail(raw);
        }

        var prefixMatch = TcpipPrefix().Match(segments[0]);
        if (!prefixMatch.Success)
        {
            return Fail(raw);
        }

        var boardText = prefixMatch.Groups["board"].Value;
        var board = boardText.Length == 0 ? 0 : int.Parse(boardText, CultureInfo.InvariantCulture);

        var host = segments[1];
        if (string.IsNullOrEmpty(host))
        {
            return Fail(raw);
        }

        string lanDevice;
        string suffix;
        if (segments.Length == 4)
        {
            lanDevice = segments[2];
            suffix = segments[3];
        }
        else
        {
            lanDevice = "inst0";
            suffix = segments[2];
        }

        if (string.IsNullOrEmpty(lanDevice) || suffix != InstrSuffix)
        {
            return Fail(raw);
        }

        return Result.Success<VisaResource, VisaResourceError>(new Tcpip(board, host, lanDevice));
    }

    private static Result<VisaResource, VisaResourceError> Fail(string raw) =>
        Result.Failure<VisaResource, VisaResourceError>(new InvalidVisaResourceFormat(raw));
}
