using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// The production VISA adapters on a machine where no VISA runtime may be
/// installed. A failed call must come back as a value and leave nothing
/// behind that can end the process later, when the garbage collector
/// finalizes it.
/// </summary>
public sealed class VisaRuntimeAbsenceTests
{
    [Fact]
    public void A_failed_open_leaves_nothing_that_fails_when_finalized()
    {
        var resource = VisaResource.Parse("TCPIP0::0.0.0.0::inst0::INSTR").ShouldBeOk();

        new VisaSessionFactory().Open(resource, TimeSpan.FromMilliseconds(200));
        CollectGarbage();
    }

    [Fact]
    public void A_failed_find_leaves_nothing_that_fails_when_finalized()
    {
        new VisaResourceFinder().Find("USB?*::0xFFFF::0xFFFF::?*::INSTR");
        CollectGarbage();
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
