using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IviCli.Application.Telemetry;

/// <summary>
/// Central definition of ActivitySource and Meter instruments used
/// across the codebase (ADR 0040). All public names follow the
/// OTel semantic-convention pattern <c>ivi.&lt;subject&gt;.&lt;verb&gt;</c>
/// so a single OTLP exporter pipeline can be configured by
/// subscribing to the parent prefix.
/// </summary>
public static class IviCliTelemetry
{
    /// <summary>Common parent name used in attributes and dashboards.</summary>
    public const string ServiceNamespace = "IviCli";

    /// <summary>ActivitySource for per-backend SCPI op spans (Open/Close/Write/Query/Read).</summary>
    public static readonly ActivitySource Backend = new(ServiceNamespace + ".Backend");

    /// <summary>ActivitySource for per-connection gateway server spans (HiSlip / Vxi11 / Socket).</summary>
    public static readonly ActivitySource Gateway = new(ServiceNamespace + ".Gateway");

    /// <summary>Meter for cross-cutting counters / gauges / histograms.</summary>
    public static readonly Meter Meter = new(ServiceNamespace);

    /// <summary>Histogram of backend op duration (ms). Tags: <c>op</c>, <c>device</c>, <c>outcome</c>.</summary>
    public static readonly Histogram<double> BackendOpDurationMs = Meter.CreateHistogram<double>(
        "ivi.backend.op_duration",
        unit: "ms",
        description: "Duration of an IIviBackend operation, by op + device + ok/error outcome."
    );

    /// <summary>Counter of pool entries closed by the idle / LRU sweep.</summary>
    public static readonly Counter<long> PoolEvictions = Meter.CreateCounter<long>(
        "ivi.pool.evictions",
        description: "Backend session pool entries closed by idle / LRU eviction (ADR 0038)."
    );

    /// <summary>Counter of pool leases that timed out waiting for the semaphore.</summary>
    public static readonly Counter<long> PoolLeaseWaitTimeouts = Meter.CreateCounter<long>(
        "ivi.pool.lease_wait_timeouts",
        description: "Pool lease attempts that exceeded device.Timeout (ADR 0038)."
    );

    /// <summary>
    /// Registers an observable gauge sampling the supplied snapshot
    /// function. Used by <c>PoolingBackendFactory</c> to expose the
    /// current cached-entry count without taking a hard dependency on
    /// any metrics registry.
    /// </summary>
    public static void RegisterPoolCachedEntriesGauge(Func<int> snapshot)
    {
        Meter.CreateObservableGauge(
            "ivi.pool.cached_entries",
            () => snapshot(),
            description: "Backend session pool entries currently cached (ADR 0038)."
        );
    }
}
