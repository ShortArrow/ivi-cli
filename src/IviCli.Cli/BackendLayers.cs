using IviCli.Application.Backends;
using IviCli.Domain.Configuration;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli;

/// <summary>
/// The decorators the CLI puts around the factory that routes each device
/// to its transport, innermost first:
/// <list type="number">
/// <item><see cref="InstrumentingBackendFactory"/>, always (ADR 0040);</item>
/// <item>the plugin layer, when plugins registered backends (ADR 0013);</item>
/// <item><see cref="DeviceTimeoutBackendFactory"/>, always, so every
/// backend, built in or from a plugin, has each operation bounded by the
/// device's timeout (ADR 0053);</item>
/// <item><see cref="PoolingBackendFactory"/>, when the pool is enabled
/// (ADR 0038). It wraps the timeout layer, so a session dropped after a
/// timeout is reopened under the lease the pool already holds.</item>
/// </list>
/// Capture, when enabled, wraps the result (ADR 0031).
/// </summary>
public static class BackendLayers
{
    /// <summary>
    /// Wraps <paramref name="routing"/> in the layers above.
    /// <paramref name="plugins"/> wraps a factory in the plugin layer, or is
    /// null when no plugin registered a backend; <paramref name="pool"/>
    /// installs the pool when it is present and enabled.
    /// </summary>
    public static IBackendFactory Compose(
        IBackendFactory routing,
        Func<IBackendFactory, IBackendFactory>? plugins,
        PoolConfig? pool,
        TimeProvider time,
        ILogger<PoolingBackendFactory>? poolLogger
    )
    {
        IBackendFactory factory = new InstrumentingBackendFactory(routing);
        if (plugins is not null)
        {
            factory = plugins(factory);
        }
        factory = new DeviceTimeoutBackendFactory(factory, time);
        return pool is { Enabled: true }
            ? new PoolingBackendFactory(factory, pool, time, poolLogger)
            : factory;
    }
}
