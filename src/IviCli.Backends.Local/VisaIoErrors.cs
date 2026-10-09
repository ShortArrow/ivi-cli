using Ivi.Visa;

namespace IviCli.Backends.Local;

/// <summary>Classifies an exception the VISA runtime throws during I/O.</summary>
public static class VisaIoErrors
{
    /// <summary>
    /// A timeout the runtime reports (VI_ERROR_TMO) becomes
    /// <see cref="LocalVisaTimeout"/> carrying <paramref name="ioTimeout"/>;
    /// anything else is a <see cref="LocalVisaIoFailure"/>.
    /// </summary>
    public static LocalVisaError From(Exception ex, TimeSpan ioTimeout) =>
        ex is IOTimeoutException || ex is NativeVisaException { ErrorCode: NativeErrorCode.Timeout }
            ? new LocalVisaTimeout(ioTimeout, ex)
            : new LocalVisaIoFailure(ex.Message, ex);
}
