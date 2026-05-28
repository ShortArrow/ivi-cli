using IviCli.Application.Telemetry;
using IviCli.Domain.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IviCli.Cli.Telemetry;

/// <summary>
/// Composition-root helper that installs OpenTelemetry traces +
/// metrics pipelines based on a <see cref="TelemetryConfig"/> (ADR
/// 0040). When telemetry is disabled the method is a no-op so the
/// CLI never pays for instrumentation it doesn't need.
/// </summary>
public static class TelemetryBootstrapper
{
    /// <summary>
    /// Adds OTel pipelines to <paramref name="services"/> when
    /// <paramref name="config"/> requests telemetry. Subscribes to
    /// <see cref="IviCliTelemetry.Backend"/>,
    /// <see cref="IviCliTelemetry.Gateway"/>, and
    /// <see cref="IviCliTelemetry.Meter"/>; adds AspNetCore HTTP
    /// instrumentation; configures an OTLP exporter whose endpoint
    /// follows (in order): <see cref="TelemetryConfig.OtlpEndpoint"/>,
    /// the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var, the OTel SDK
    /// default <c>http://localhost:4317</c>.
    /// </summary>
    public static void Install(IServiceCollection services, TelemetryConfig config)
    {
        if (!config.Enabled)
        {
            return;
        }

        var resource = ResourceBuilder.CreateDefault().AddService(serviceName: config.ServiceName);

        var otelBuilder = services
            .AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(config.ServiceName));

        if (config.TracesEnabled)
        {
            otelBuilder.WithTracing(t =>
            {
                t.AddSource(IviCliTelemetry.Backend.Name);
                t.AddSource(IviCliTelemetry.Gateway.Name);
                t.AddAspNetCoreInstrumentation();
                ConfigureOtlpEndpoint(t, config.OtlpEndpoint);
            });
        }

        if (config.MetricsEnabled)
        {
            otelBuilder.WithMetrics(m =>
            {
                m.AddMeter(IviCliTelemetry.Meter.Name);
                m.AddAspNetCoreInstrumentation();
                ConfigureOtlpMetricsEndpoint(m, config.OtlpEndpoint);
            });
        }
    }

    private static void ConfigureOtlpEndpoint(
        TracerProviderBuilder builder,
        string? endpointFromConfig
    )
    {
        builder.AddOtlpExporter(o =>
        {
            if (endpointFromConfig is not null)
            {
                o.Endpoint = new Uri(endpointFromConfig);
            }
            // OTLP exporter respects OTEL_EXPORTER_OTLP_ENDPOINT by
            // default when no explicit Endpoint is supplied — no
            // additional logic required here.
        });
    }

    private static void ConfigureOtlpMetricsEndpoint(
        MeterProviderBuilder builder,
        string? endpointFromConfig
    )
    {
        builder.AddOtlpExporter(o =>
        {
            if (endpointFromConfig is not null)
            {
                o.Endpoint = new Uri(endpointFromConfig);
            }
        });
    }
}
