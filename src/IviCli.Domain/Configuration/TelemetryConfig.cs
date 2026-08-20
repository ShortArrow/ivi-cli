namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[telemetry]</c> section of a configuration document (ADR
/// 0040). Controls OpenTelemetry trace + metric export. Telemetry is
/// fully opt-in; <see cref="TelemetryConfig.Default"/> is "disabled"
/// and no exporter is installed.
/// </summary>
public sealed record TelemetryConfig
{
    /// <summary>Telemetry-disabled default — no exporter, no extra overhead.</summary>
    public static TelemetryConfig Default { get; } =
        new(
            enabled: false,
            otlpEndpoint: null,
            serviceName: "ivi-cli",
            tracesEnabled: true,
            metricsEnabled: true,
            hislipPropagationEnabled: false
        );

    /// <summary>When <see langword="false"/> the composition root skips OTel setup entirely.</summary>
    public bool Enabled { get; }

    /// <summary>OTLP endpoint URL (gRPC or HTTP/protobuf). Null falls back to the OTel-standard env var.</summary>
    public string? OtlpEndpoint { get; }

    /// <summary>Logical service name reported on every span and metric.</summary>
    public string ServiceName { get; }

    /// <summary>When false, the trace pipeline is not built (metrics may still flow).</summary>
    public bool TracesEnabled { get; }

    /// <summary>When false, the metrics pipeline is not built (traces may still flow).</summary>
    public bool MetricsEnabled { get; }

    /// <summary>
    /// When <see langword="true"/>, the HiSLIP client backend precedes each
    /// operation with a VendorTraceContext message (vendor-specific type 128)
    /// carrying the current W3C trace context, so an ivi-cli gateway joins
    /// the caller's trace. Off by default: a conforming foreign server
    /// answers an unrecognized vendor message with a non-fatal Error
    /// (IVI-6.1 Table 16, code 3), so enable it only when the HiSLIP peer
    /// is an ivi-cli gateway.
    /// </summary>
    public bool HiSlipPropagationEnabled { get; }

    private TelemetryConfig(
        bool enabled,
        string? otlpEndpoint,
        string serviceName,
        bool tracesEnabled,
        bool metricsEnabled,
        bool hislipPropagationEnabled
    )
    {
        Enabled = enabled;
        OtlpEndpoint = otlpEndpoint;
        ServiceName = serviceName;
        TracesEnabled = tracesEnabled;
        MetricsEnabled = metricsEnabled;
        HiSlipPropagationEnabled = hislipPropagationEnabled;
    }

    /// <summary>Validates and constructs a <see cref="TelemetryConfig"/>.</summary>
    public static Result<TelemetryConfig, TelemetryConfigError> From(
        bool enabled,
        string? otlpEndpoint,
        string serviceName,
        bool tracesEnabled,
        bool metricsEnabled,
        bool hislipPropagationEnabled = false
    )
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Result.Failure<TelemetryConfig, TelemetryConfigError>(
                new TelemetryServiceNameEmpty()
            );
        }
        if (enabled && !tracesEnabled && !metricsEnabled)
        {
            return Result.Failure<TelemetryConfig, TelemetryConfigError>(
                new TelemetryEnabledButAllSignalsOff()
            );
        }
        if (
            !string.IsNullOrWhiteSpace(otlpEndpoint)
            && !Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out _)
        )
        {
            return Result.Failure<TelemetryConfig, TelemetryConfigError>(
                new TelemetryInvalidOtlpEndpoint(otlpEndpoint)
            );
        }
        return Result.Success<TelemetryConfig, TelemetryConfigError>(
            new TelemetryConfig(
                enabled,
                NullIfEmpty(otlpEndpoint),
                serviceName,
                tracesEnabled,
                metricsEnabled,
                hislipPropagationEnabled
            )
        );
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Errors that can surface from <see cref="TelemetryConfig.From"/>.</summary>
public abstract record TelemetryConfigError : IviError
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

/// <summary>Service name was blank.</summary>
public sealed record TelemetryServiceNameEmpty : TelemetryConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "[telemetry].service_name must not be empty";
}

/// <summary>Telemetry is enabled but both trace and metric pipelines were disabled.</summary>
public sealed record TelemetryEnabledButAllSignalsOff : TelemetryConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "[telemetry].enabled is true but both traces_enabled and metrics_enabled are false";
}

/// <summary>OTLP endpoint URL did not parse.</summary>
public sealed record TelemetryInvalidOtlpEndpoint(string Value) : TelemetryConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "[telemetry].otlp_endpoint is not a valid absolute URI: {Value}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Value };
}
