using System.Collections.Immutable;
using IviCli.Application.Capture;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Application service that maps a stream of <see cref="TrafficEvent"/>
/// records (Batch F capture output) into a <see cref="MockScenario"/>
/// suitable for the existing replay machinery (ADR 0028 + ADR 0033).
/// Pure function; no IO.
/// </summary>
public interface ITrafficScenarioConverter
{
    /// <summary>
    /// Builds a scenario named <paramref name="name"/> from
    /// <paramref name="events"/>. When <paramref name="deviceFilter"/> is
    /// <see langword="null"/> and the events cover multiple devices,
    /// returns <see cref="ConvertTrafficMultipleDevices"/>.
    /// </summary>
    Result<MockScenario, ConvertTrafficError> Convert(
        IEnumerable<TrafficEvent> events,
        ScenarioName name,
        DeviceName? deviceFilter
    );
}

/// <summary>Errors the converter can surface.</summary>
public abstract record ConvertTrafficError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The capture file contains events from more than one device.</summary>
public sealed record ConvertTrafficMultipleDevices(ImmutableArray<string> Devices)
    : ConvertTrafficError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "capture covers multiple devices ({Devices}); pass --device to choose one";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { string.Join(", ", Devices) };
}

/// <summary>The filtered event stream produced no replayable scenes.</summary>
public sealed record ConvertTrafficNoScenes(string? DeviceFilter) : ConvertTrafficError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        DeviceFilter is null
            ? "capture contained no Write / Query events with Ok=true"
            : "no Write / Query events with Ok=true for device {DeviceFilter}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs =>
        DeviceFilter is null ? Array.Empty<object?>() : new object?[] { DeviceFilter };
}

/// <summary>
/// Default converter. Maps Write events to <see cref="RuleAction.Ack"/>
/// and Query events to <see cref="RuleAction.Respond"/>. Skips Open /
/// Close / Read / failed events (ADR 0033 §1 mapping table).
/// </summary>
public sealed class DefaultTrafficScenarioConverter : ITrafficScenarioConverter
{
    /// <inheritdoc/>
    public Result<MockScenario, ConvertTrafficError> Convert(
        IEnumerable<TrafficEvent> events,
        ScenarioName name,
        DeviceName? deviceFilter
    )
    {
        ArgumentNullException.ThrowIfNull(events);
        var seenDevices = new HashSet<string>(StringComparer.Ordinal);
        var rules = ImmutableArray.CreateBuilder<MockRule>();
        string? idnDefault = null;

        foreach (var ev in events)
        {
            if (ev.Op != TrafficOp.Write && ev.Op != TrafficOp.Query)
            {
                continue;
            }
            if (!ev.Ok)
            {
                continue;
            }
            if (
                deviceFilter is not null
                && !string.Equals(ev.Device, deviceFilter.Value, StringComparison.Ordinal)
            )
            {
                continue;
            }
            seenDevices.Add(ev.Device);

            if (ev.Data is null)
            {
                continue;
            }
            var rule =
                ev.Op == TrafficOp.Write
                    ? new MockRule(ev.Data, new RuleAction.Ack())
                    : new MockRule(ev.Data, new RuleAction.Respond(ev.Response ?? string.Empty));
            rules.Add(rule);

            if (
                ev.Op == TrafficOp.Query
                && idnDefault is null
                && string.Equals(ev.Data, "*IDN?", StringComparison.OrdinalIgnoreCase)
                && ev.Response is { Length: > 0 } resp
            )
            {
                idnDefault = resp;
            }
        }

        if (deviceFilter is null && seenDevices.Count > 1)
        {
            return Result.Failure<MockScenario, ConvertTrafficError>(
                new ConvertTrafficMultipleDevices(
                    seenDevices.OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray()
                )
            );
        }
        if (rules.Count == 0)
        {
            return Result.Failure<MockScenario, ConvertTrafficError>(
                new ConvertTrafficNoScenes(deviceFilter?.Value)
            );
        }

        return Result.Success<MockScenario, ConvertTrafficError>(
            MockScenario.SingleScene(name, idnDefault, rules.ToImmutable())
        );
    }
}
