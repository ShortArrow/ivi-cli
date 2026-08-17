using System.Threading.Channels;

namespace IviCli.Application.Backends;

/// <summary>
/// The per-device buffer a backend queues <see cref="ServiceRequest"/>s
/// in until a <c>ServiceRequestStream</c> consumer takes them. A service
/// request is a signal that the instrument wants attention now, so the
/// buffer keeps the newest <see cref="Capacity"/> requests and drops the
/// oldest when nobody is reading — a device that raises requests into a
/// gateway with no SRQ channel (raw socket, CDC-ACM) must not grow the
/// process without bound.
/// </summary>
public static class ServiceRequestBuffer
{
    /// <summary>How many undelivered requests a device holds before the oldest is dropped.</summary>
    public const int Capacity = 256;

    /// <summary>Creates a buffer with the policy above; writes always succeed.</summary>
    public static Channel<ServiceRequest> Create() =>
        Channel.CreateBounded<ServiceRequest>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest }
        );
}
