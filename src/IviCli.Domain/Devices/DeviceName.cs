using System.Text.RegularExpressions;

namespace IviCli.Domain.Devices;

/// <summary>
/// A short, human-readable alias that identifies a configured device
/// (for example <c>psu1</c>, <c>scope1</c>).
/// </summary>
/// <remarks>
/// Construct instances through <see cref="From(string)"/>; the constructor is
/// private so that every <see cref="DeviceName"/> in flight is known to have
/// passed the validation rules enforced here.
/// </remarks>
public sealed partial record DeviceName
{
    /// <summary>The maximum permitted length, in characters.</summary>
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex AllowedPattern();

    /// <summary>The underlying canonical string value.</summary>
    public string Value { get; }

    private DeviceName(string value) => Value = value;

    /// <summary>
    /// Parses and validates a raw string into a <see cref="DeviceName"/>.
    /// </summary>
    /// <param name="raw">The candidate name.</param>
    /// <returns>
    /// <see cref="Result{T, TError}.Ok"/> wrapping the constructed
    /// <see cref="DeviceName"/> on success; otherwise
    /// <see cref="Result{T, TError}.Error"/> wrapping <see cref="InvalidDeviceNameFormat"/>.
    /// </returns>
    public static Result<DeviceName, DeviceError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength || !AllowedPattern().IsMatch(raw))
        {
            return Result.Failure<DeviceName, DeviceError>(new InvalidDeviceNameFormat(raw));
        }
        return Result.Success<DeviceName, DeviceError>(new DeviceName(raw));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
