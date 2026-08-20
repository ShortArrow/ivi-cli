using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Configuration;

public sealed class TelemetryConfigTests
{
    [Fact]
    public void Default_is_disabled_with_ivi_cli_service_name()
    {
        TelemetryConfig.Default.Enabled.ShouldBeFalse();
        TelemetryConfig.Default.OtlpEndpoint.ShouldBeNull();
        TelemetryConfig.Default.ServiceName.ShouldBe("ivi-cli");
        TelemetryConfig.Default.TracesEnabled.ShouldBeTrue();
        TelemetryConfig.Default.MetricsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void From_enabled_with_endpoint_succeeds()
    {
        var result = TelemetryConfig.From(
            enabled: true,
            otlpEndpoint: "http://otel-collector:4317",
            serviceName: "ivi-cli",
            tracesEnabled: true,
            metricsEnabled: true
        );
        var cfg = result.ShouldBeOk();
        cfg.Enabled.ShouldBeTrue();
        cfg.OtlpEndpoint.ShouldBe("http://otel-collector:4317");
    }

    [Fact]
    public void From_empty_service_name_fails()
    {
        var result = TelemetryConfig.From(true, null, "", true, true);
        result.ShouldBeError().ShouldBeOfType<TelemetryServiceNameEmpty>();
    }

    [Fact]
    public void From_enabled_with_both_signals_off_fails()
    {
        var result = TelemetryConfig.From(true, null, "ivi-cli", false, false);
        result.ShouldBeError().ShouldBeOfType<TelemetryEnabledButAllSignalsOff>();
    }

    [Fact]
    public void From_invalid_otlp_endpoint_fails()
    {
        var result = TelemetryConfig.From(true, "not a uri", "ivi-cli", true, true);
        result.ShouldBeError().ShouldBeOfType<TelemetryInvalidOtlpEndpoint>();
    }

    [Fact]
    public void Default_does_not_propagate_hislip_trace_context()
    {
        TelemetryConfig.Default.HiSlipPropagationEnabled.ShouldBeFalse();
    }

    [Fact]
    public void From_can_opt_in_to_hislip_trace_context_propagation()
    {
        var result = TelemetryConfig.From(
            enabled: true,
            otlpEndpoint: null,
            serviceName: "ivi-cli",
            tracesEnabled: true,
            metricsEnabled: true,
            hislipPropagationEnabled: true
        );
        result.ShouldBeOk().HiSlipPropagationEnabled.ShouldBeTrue();
    }

    [Fact]
    public void From_disabled_can_keep_signals_off()
    {
        // Enabled=false means we don't enforce the "at least one signal" rule.
        var result = TelemetryConfig.From(false, null, "ivi-cli", false, false);
        result.ShouldBeOk();
    }
}
