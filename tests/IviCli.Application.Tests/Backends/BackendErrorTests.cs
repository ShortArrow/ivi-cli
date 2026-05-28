using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

public sealed class BackendErrorTests
{
    [Fact]
    public void PoolWaitTimeout_carries_device_and_elapsed_in_log_args()
    {
        var device = DeviceName.From("psu1").ShouldBeOk();
        BackendError err = new PoolWaitTimeout(device, TimeSpan.FromMilliseconds(250));

        err.Severity.ShouldBe(LogSeverity.Warning);
        err.Message.ShouldContain("{Waited}");
        err.Message.ShouldContain("{Device}");
        err.LogArgs.Count.ShouldBe(2);
        err.LogArgs[0].ShouldBe(TimeSpan.FromMilliseconds(250));
        err.LogArgs[1].ShouldBe(device);
    }
}
