using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Local;
using IviCli.Backends.Serial;
using IviCli.Backends.Socket;
using IviCli.Backends.Vxi11;
using IviCli.Infrastructure.Backends;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Cli;

/// <summary>
/// The <see cref="DefaultBackendFactory"/> the CLI routes devices with,
/// over the built-in backends; an ASRL board goes to an
/// <see cref="AsrlBoardBackend"/> over the Serial and VISA backends
/// (ADR 0055 §2, §3).
/// </summary>
public static class TransportRouting
{
    /// <summary>
    /// Routes devices to the backends <paramref name="services"/> holds,
    /// and anything those do not take to <paramref name="fallback"/>. With
    /// <paramref name="mockOnly"/> (<c>IVICLI_MOCK_ONLY</c>) every device
    /// goes to <paramref name="fallback"/>, so nothing connects out
    /// (ADR 0018). <paramref name="propagateTraceContext"/> makes the HiSLIP
    /// backend precede each operation with the caller's W3C trace context,
    /// so an ivi-cli gateway joins the trace (ADR 0040); foreign HiSLIP
    /// servers answer that vendor message with a non-fatal error.
    /// </summary>
    public static IBackendFactory Create(
        IServiceProvider services,
        IIviBackend fallback,
        bool mockOnly,
        bool propagateTraceContext
    )
    {
        if (mockOnly)
        {
            return new DefaultBackendFactory(fallback);
        }
        var local = services.GetRequiredService<LocalBackend>();
        var serial = services.GetRequiredService<SerialBackend>();
        var hislip = services.GetRequiredService<HiSlipBackend>();
        hislip.PropagateTraceContext = propagateTraceContext;
        return new DefaultBackendFactory(
            fallbackBackend: fallback,
            localBackend: local,
            hislipBackend: hislip,
            socketBackend: services.GetRequiredService<SocketBackend>(),
            vxi11Backend: services.GetRequiredService<Vxi11Backend>(),
            serialBackend: serial,
            asrlBoardBackend: new AsrlBoardBackend(
                services.GetRequiredService<IConfigStore>(),
                serial,
                local
            )
        );
    }
}
