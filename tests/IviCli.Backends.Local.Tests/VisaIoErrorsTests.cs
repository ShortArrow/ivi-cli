using Ivi.Visa;
using IviCli.Backends.Local;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// A timeout the VISA runtime reports (VI_ERROR_TMO, VPP-4.3 §6.1.1 and
/// §6.1.4) is told apart from any other I/O failure.
/// </summary>
public sealed class VisaIoErrorsTests
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public void An_IOTimeoutException_is_a_timeout()
    {
        var ex = new IOTimeoutException(0, []);

        VisaIoErrors
            .From(ex, IoTimeout)
            .ShouldBeOfType<LocalVisaTimeout>()
            .ShouldBe(new LocalVisaTimeout(IoTimeout, ex));
    }

    [Fact]
    public void A_native_VI_ERROR_TMO_is_a_timeout()
    {
        var ex = new NativeVisaException(NativeErrorCode.Timeout);

        VisaIoErrors.From(ex, IoTimeout).ShouldBeOfType<LocalVisaTimeout>().Inner.ShouldBe(ex);
    }

    [Fact]
    public void Another_native_error_is_an_IO_failure()
    {
        var ex = new NativeVisaException(NativeErrorCode.IOError);

        VisaIoErrors.From(ex, IoTimeout).ShouldBeOfType<LocalVisaIoFailure>().Cause.ShouldBe(ex);
    }
}
