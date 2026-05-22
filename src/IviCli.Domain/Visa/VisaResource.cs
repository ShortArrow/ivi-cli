using System.Globalization;
using System.Text.RegularExpressions;

namespace IviCli.Domain.Visa;

/// <summary>
/// A parsed VISA resource — the structured form of strings such as
/// <c>TCPIP0::192.168.0.10::inst0::INSTR</c> or
/// <c>USB0::0x0699::0x0408::C012345::INSTR</c>.
/// </summary>
/// <remarks>
/// Variants are added as new transports are supported. Cycles 1, 2 and 3
/// introduce <see cref="Tcpip"/>, <see cref="Usb"/> and <see cref="Gpib"/>;
/// SOCKET and other transports follow in later cycles.
/// </remarks>
public abstract partial record VisaResource
{
    private VisaResource() { }

    /// <summary>
    /// Returns a redacted form of this resource suitable for logging at
    /// <c>Information</c> or above, per ADR 0017 §3. Variable, sensitive
    /// segments (hostnames, IP literals, serial numbers) are replaced with
    /// <c>***</c>; the transport prefix and well-known suffix remain so
    /// operators can still tell variants apart in logs.
    /// </summary>
    public abstract string ToLogString();

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
    public sealed record Tcpip(int Board, string Host, string LanDevice) : VisaResource
    {
        /// <inheritdoc/>
        public override string ToLogString() =>
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"TCPIP{Board}::***::{LanDevice}::INSTR"
            );
    }

    /// <summary>
    /// A USB-attached instrument resource of the form
    /// <c>USB[board]::vendor_id::product_id::serial_number[::interface_number]::INSTR</c>.
    /// </summary>
    /// <param name="Board">The USB interface number (defaults to <c>0</c>).</param>
    /// <param name="VendorId">The USB-IF vendor ID as a canonical <c>0xNNNN</c> string (lowercase).</param>
    /// <param name="ProductId">The USB-IF product ID as a canonical <c>0xNNNN</c> string (lowercase).</param>
    /// <param name="SerialNumber">The instrument serial number, vendor-defined.</param>
    /// <param name="InterfaceNumber">The optional USB interface number; <see langword="null"/> when omitted.</param>
    public sealed record Usb(
        int Board,
        string VendorId,
        string ProductId,
        string SerialNumber,
        int? InterfaceNumber
    ) : VisaResource
    {
        /// <inheritdoc/>
        public override string ToLogString() =>
            InterfaceNumber is { } iface
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"USB{Board}::{VendorId}::{ProductId}::***::{iface}::INSTR"
                )
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"USB{Board}::{VendorId}::{ProductId}::***::INSTR"
                );
    }

    /// <summary>
    /// A GPIB (IEEE-488) instrument resource of the form
    /// <c>GPIB[board]::primary_address[::secondary_address]::INSTR</c>.
    /// </summary>
    /// <param name="Board">The GPIB interface number (defaults to <c>0</c>).</param>
    /// <param name="PrimaryAddress">The GPIB primary address (0–30).</param>
    /// <param name="SecondaryAddress">
    /// The optional GPIB secondary address (0–30); <see langword="null"/> when omitted.
    /// </param>
    public sealed record Gpib(int Board, int PrimaryAddress, int? SecondaryAddress) : VisaResource
    {
        /// <inheritdoc/>
        public override string ToLogString() =>
            SecondaryAddress is { } secondary
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"GPIB{Board}::{PrimaryAddress}::{secondary}::INSTR"
                )
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"GPIB{Board}::{PrimaryAddress}::INSTR"
                );
    }

    /// <summary>Inclusive upper bound for GPIB primary and secondary addresses.</summary>
    public const int MaxGpibAddress = 30;

    [GeneratedRegex("^TCPIP(?<board>[0-9]*)$")]
    private static partial Regex TcpipPrefix();

    [GeneratedRegex("^USB(?<board>[0-9]*)$")]
    private static partial Regex UsbPrefix();

    [GeneratedRegex("^GPIB(?<board>[0-9]*)$")]
    private static partial Regex GpibPrefix();

    [GeneratedRegex("^0[xX](?<hex>[0-9a-fA-F]{4})$")]
    private static partial Regex UsbIdentifier();

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
        if (segments.Length == 0)
        {
            return Fail(raw);
        }

        if (segments[0].StartsWith("TCPIP", StringComparison.Ordinal))
        {
            return ParseTcpip(raw, segments);
        }

        if (segments[0].StartsWith("USB", StringComparison.Ordinal))
        {
            return ParseUsb(raw, segments);
        }

        if (segments[0].StartsWith("GPIB", StringComparison.Ordinal))
        {
            return ParseGpib(raw, segments);
        }

        return Fail(raw);
    }

    private static Result<VisaResource, VisaResourceError> ParseTcpip(string raw, string[] segments)
    {
        if (segments.Length is < 3 or > 4)
        {
            return Fail(raw);
        }

        var prefixMatch = TcpipPrefix().Match(segments[0]);
        if (!prefixMatch.Success)
        {
            return Fail(raw);
        }

        var board = ParseBoard(prefixMatch);

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

    private static Result<VisaResource, VisaResourceError> ParseUsb(string raw, string[] segments)
    {
        // Accepted forms:
        //   USB[N]::vendor::product::serial::INSTR              (5 segments)
        //   USB[N]::vendor::product::serial::interface::INSTR   (6 segments)
        if (segments.Length is < 5 or > 6)
        {
            return Fail(raw);
        }

        var prefixMatch = UsbPrefix().Match(segments[0]);
        if (!prefixMatch.Success)
        {
            return Fail(raw);
        }

        var board = ParseBoard(prefixMatch);

        if (
            !TryNormaliseUsbId(segments[1], out var vendorId)
            || !TryNormaliseUsbId(segments[2], out var productId)
        )
        {
            return Fail(raw);
        }

        var serialNumber = segments[3];
        if (string.IsNullOrEmpty(serialNumber))
        {
            return Fail(raw);
        }

        int? interfaceNumber;
        string suffix;
        if (segments.Length == 6)
        {
            if (
                !int.TryParse(
                    segments[4],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ifn
                )
            )
            {
                return Fail(raw);
            }
            interfaceNumber = ifn;
            suffix = segments[5];
        }
        else
        {
            interfaceNumber = null;
            suffix = segments[4];
        }

        if (suffix != InstrSuffix)
        {
            return Fail(raw);
        }

        return Result.Success<VisaResource, VisaResourceError>(
            new Usb(board, vendorId, productId, serialNumber, interfaceNumber)
        );
    }

    private static Result<VisaResource, VisaResourceError> ParseGpib(string raw, string[] segments)
    {
        // Accepted forms:
        //   GPIB[N]::primary::INSTR              (3 segments)
        //   GPIB[N]::primary::secondary::INSTR   (4 segments)
        if (segments.Length is < 3 or > 4)
        {
            return Fail(raw);
        }

        var prefixMatch = GpibPrefix().Match(segments[0]);
        if (!prefixMatch.Success)
        {
            return Fail(raw);
        }

        var board = ParseBoard(prefixMatch);

        if (!TryParseGpibAddress(segments[1], out var primaryAddress))
        {
            return Fail(raw);
        }

        int? secondaryAddress;
        string suffix;
        if (segments.Length == 4)
        {
            if (!TryParseGpibAddress(segments[2], out var secondary))
            {
                return Fail(raw);
            }
            secondaryAddress = secondary;
            suffix = segments[3];
        }
        else
        {
            secondaryAddress = null;
            suffix = segments[2];
        }

        if (suffix != InstrSuffix)
        {
            return Fail(raw);
        }

        return Result.Success<VisaResource, VisaResourceError>(
            new Gpib(board, primaryAddress, secondaryAddress)
        );
    }

    private static bool TryParseGpibAddress(string raw, out int address)
    {
        if (
            int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0
            && parsed <= MaxGpibAddress
        )
        {
            address = parsed;
            return true;
        }

        address = 0;
        return false;
    }

    private static int ParseBoard(Match prefixMatch)
    {
        var boardText = prefixMatch.Groups["board"].Value;
        return boardText.Length == 0 ? 0 : int.Parse(boardText, CultureInfo.InvariantCulture);
    }

    private static bool TryNormaliseUsbId(string raw, out string normalised)
    {
        var match = UsbIdentifier().Match(raw);
        if (!match.Success)
        {
            normalised = string.Empty;
            return false;
        }

        normalised = "0x" + match.Groups["hex"].Value.ToLowerInvariant();
        return true;
    }

    private static Result<VisaResource, VisaResourceError> Fail(string raw) =>
        Result.Failure<VisaResource, VisaResourceError>(new InvalidVisaResourceFormat(raw));
}
