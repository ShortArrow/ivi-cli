using System.Diagnostics;
using System.Text;
using IviCli.Application.Telemetry;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;

namespace IviCli.Server;

/// <summary>
/// Gateway-side Activity emission (ADR 0040): one <c>gateway.session</c>
/// span per accepted connection / link and one <c>gateway.message</c> span
/// per handled operation, on <see cref="IviCliTelemetry.Gateway"/>. Backend
/// spans started while a message span is current nest under it, so a trace
/// shows gateway → backend → device. With no listener attached every call
/// returns null and costs nothing.
/// </summary>
internal static class GatewayTelemetry
{
    /// <summary>Starts the per-connection span. Dispose it when the connection ends.</summary>
    public static Activity? StartSession(string transport, ServerName server, DeviceName device)
    {
        var activity = IviCliTelemetry.Gateway.StartActivity(
            "gateway.session",
            ActivityKind.Server
        );
        Tag(activity, transport, server, device);
        return activity;
    }

    /// <summary>
    /// Starts a per-operation span. With a remote trace context (from a
    /// HiSLIP VendorTraceContext message) the span is parented there, so the
    /// caller's trace shows the gateway leg, and the session span is
    /// attached as a link instead; otherwise the session span is the parent.
    /// </summary>
    public static Activity? StartMessage(
        string transport,
        string operation,
        ServerName server,
        DeviceName device,
        Activity? session,
        ActivityContext remoteParent
    )
    {
        Activity? activity;
        if (remoteParent != default)
        {
            var links = session is null
                ? Array.Empty<ActivityLink>()
                : new[] { new ActivityLink(session.Context) };
            activity = IviCliTelemetry.Gateway.StartActivity(
                "gateway.message",
                ActivityKind.Server,
                remoteParent,
                links: links
            );
        }
        else
        {
            activity = IviCliTelemetry.Gateway.StartActivity(
                "gateway.message",
                ActivityKind.Server,
                session?.Context ?? default
            );
        }
        Tag(activity, transport, server, device);
        activity?.SetTag("ivi.operation", operation);
        return activity;
    }

    /// <summary>Records the operation outcome; the caller's <c>using</c> stops the span.</summary>
    public static void Complete(Activity? activity, bool ok)
    {
        activity?.SetTag("outcome", ok ? "ok" : "error");
        if (!ok)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    /// <summary>
    /// Parses a VendorTraceContext payload — a W3C <c>traceparent</c>,
    /// optionally followed by a newline and a <c>tracestate</c> — into a
    /// remote <see cref="ActivityContext"/>. Returns <c>default</c> when
    /// the payload does not parse; the message span then falls back to the
    /// session parent rather than failing the connection.
    /// </summary>
    public static ActivityContext ParseRemoteContext(ReadOnlySpan<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        var traceparent = (newline < 0 ? text : text[..newline]).Trim();
        var tracestate = newline < 0 ? null : text[(newline + 1)..].Trim();
        return ActivityContext.TryParse(
            traceparent,
            string.IsNullOrEmpty(tracestate) ? null : tracestate,
            isRemote: true,
            out var context
        )
            ? context
            : default;
    }

    private static void Tag(
        Activity? activity,
        string transport,
        ServerName server,
        DeviceName device
    )
    {
        activity?.SetTag("ivi.transport", transport);
        activity?.SetTag("ivi.server", server.Value);
        activity?.SetTag("ivi.device", device.Value);
    }
}
