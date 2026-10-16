using System.Collections.Immutable;
using System.Globalization;
using System.IO.Ports;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Serial;

/// <summary>
/// Discovery scanner for serial ports (ADR 0055 §3). Reports each port the
/// system lists as <c>ASRL&lt;port&gt;::INSTR</c>, in port-name order, with
/// the board that names it or, for a port no board names, the
/// <c>visa asrl add</c> line that registers it under a free board number.
/// Each such port gets a different number, so the lines can be run one
/// after another. A port whose name is only digits cannot be named by an
/// ASRL resource and is left out.
/// </summary>
public sealed class SerialPortScanner : IBackendScanner
{
    private readonly IConfigStore _config;
    private readonly ISerialPorts _ports;
    private readonly Func<IReadOnlyCollection<string>> _portNames;

    /// <summary>Lists the ports <see cref="SerialPort.GetPortNames"/> reports.</summary>
    public SerialPortScanner(IConfigStore config, ISerialPorts ports)
        : this(config, ports, SerialPort.GetPortNames) { }

    /// <summary>Lists the ports <paramref name="portNames"/> reports.</summary>
    public SerialPortScanner(
        IConfigStore config,
        ISerialPorts ports,
        Func<IReadOnlyCollection<string>> portNames
    )
    {
        _config = config;
        _ports = ports;
        _portNames = portNames;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        ScanOptions options,
        CancellationToken ct
    )
    {
        var boards = await _config.LoadAsync(ct) is Result<ConfigDocument, ConfigStoreError>.Ok ok
            ? ok.Value.AsrlBoards
            : (ImmutableArray<AsrlBoardRegistration>?)null;
        var boardOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var registration in boards ?? [])
        {
            boardOf.TryAdd(_ports.Resolve(registration.Port), registration.Board);
        }
        var taken = boards?.Select(b => b.Board).ToHashSet() ?? [];
        var nextFree = 1;

        string Register(string port)
        {
            while (!taken.Add(nextFree))
            {
                nextFree++;
            }
            return string.Create(
                CultureInfo.InvariantCulture,
                $"register with: ivicli visa asrl add {nextFree} {port}"
            );
        }

        var discovered = ImmutableArray.CreateBuilder<DiscoveredResource>();
        foreach (var port in _portNames().Order(StringComparer.Ordinal))
        {
            if (
                VisaResource.Parse($"ASRL{port}::INSTR")
                is not Result<VisaResource, VisaResourceError>.Ok
                {
                    Value: VisaResource.AsrlPort resource
                }
            )
            {
                continue;
            }
            var detail =
                boards is null ? null
                : boardOf.TryGetValue(_ports.Resolve(port), out var board)
                    ? string.Create(CultureInfo.InvariantCulture, $"board {board}")
                : Register(port);
            discovered.Add(new DiscoveredResource(resource, Idn: null, detail));
        }
        return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
            discovered.ToImmutable()
        );
    }
}
