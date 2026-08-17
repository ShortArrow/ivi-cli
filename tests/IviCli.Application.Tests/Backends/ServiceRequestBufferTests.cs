using IviCli.Application.Backends;
using IviCli.Domain.Devices;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

/// <summary>
/// The service-request buffer keeps a process from growing when a
/// device raises requests that no gateway consumes: it holds the newest
/// <see cref="ServiceRequestBuffer.Capacity"/> and lets the oldest go.
/// </summary>
public sealed class ServiceRequestBufferTests
{
    [Fact]
    public async Task Requests_nobody_reads_are_capped_keeping_the_newest()
    {
        var device = DeviceName.From("dut").ShouldBeOk();
        var buffer = ServiceRequestBuffer.Create();
        var overflow = 3;

        for (var i = 0; i < ServiceRequestBuffer.Capacity + overflow; i++)
        {
            buffer
                .Writer.TryWrite(
                    new ServiceRequest(device, (byte)(i % 256), DateTimeOffset.UnixEpoch)
                )
                .ShouldBeTrue();
        }

        var kept = new List<ServiceRequest>();
        while (buffer.Reader.TryRead(out var srq))
        {
            kept.Add(srq);
        }
        kept.Count.ShouldBe(ServiceRequestBuffer.Capacity);
        kept[0].StatusByte.ShouldBe((byte)overflow);
        kept[^1].StatusByte.ShouldBe((byte)((ServiceRequestBuffer.Capacity + overflow - 1) % 256));
    }
}
