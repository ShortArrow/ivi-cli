using Ivi.Visa;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Production <see cref="IVisaSessionFactory"/> over the IVI Foundation
/// VISA.NET shared components. <see cref="GlobalResourceManager"/> locates an
/// installed vendor implementation at runtime; when none is installed, every
/// open returns <see cref="LocalVisaRuntimeMissing"/> without calling it.
/// </summary>
public sealed class VisaSessionFactory : IVisaSessionFactory
{
    /// <inheritdoc/>
    public Result<IVisaSessionHandle, LocalVisaError> Open(
        VisaResource resource,
        TimeSpan openTimeout,
        TimeSpan ioTimeout
    )
    {
        if (!VisaRuntime.IsInstalled)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing(VisaRuntime.MissingDetail)
            );
        }
        var resourceString = VisaResourceFormatter.Format(resource);
        var timeoutMs = (int)openTimeout.TotalMilliseconds;
        try
        {
            var session = GlobalResourceManager.Open(resourceString, AccessModes.None, timeoutMs);
            if (session is not IMessageBasedSession messageBased)
            {
                session.Dispose();
                return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                    new LocalVisaOpenFailure(resourceString, "resource is not message-based", null)
                );
            }
            messageBased.TimeoutMilliseconds = (int)ioTimeout.TotalMilliseconds;
            return Result.Success<IVisaSessionHandle, LocalVisaError>(
                new VisaSessionHandle(messageBased, ioTimeout)
            );
        }
        catch (Exception ex)
            when (ex is DllNotFoundException or FileNotFoundException or TypeInitializationException
            )
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing(VisaRuntime.MissingDetail)
            );
        }
        catch (Exception ex)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaOpenFailure(resourceString, ex.Message, ex)
            );
        }
    }

    private sealed class VisaSessionHandle : IVisaSessionHandle
    {
        private static readonly TimeSpan SrqWaitSlice = TimeSpan.FromMilliseconds(500);

        private readonly IMessageBasedSession _session;
        private readonly TimeSpan _ioTimeout;
        private CancellationTokenSource? _srqPumpStop;
        private bool _disposed;

        public VisaSessionHandle(IMessageBasedSession session, TimeSpan ioTimeout)
        {
            _session = session;
            _ioTimeout = ioTimeout;
        }

        public Result<Unit, LocalVisaError> Write(string text)
        {
            try
            {
                _session.FormattedIO.WriteLine(text);
                return Result.Success<Unit, LocalVisaError>(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result.Failure<Unit, LocalVisaError>(IoError(ex));
            }
        }

        public Result<string, LocalVisaError> Query(string text)
        {
            try
            {
                _session.FormattedIO.WriteLine(text);
                return Result.Success<string, LocalVisaError>(ReadResponse());
            }
            catch (Exception ex)
            {
                return Result.Failure<string, LocalVisaError>(IoError(ex));
            }
        }

        public Result<string, LocalVisaError> Read()
        {
            try
            {
                return Result.Success<string, LocalVisaError>(ReadResponse());
            }
            catch (Exception ex)
            {
                return Result.Failure<string, LocalVisaError>(IoError(ex));
            }
        }

        /// <summary>
        /// A timeout the runtime reports (VI_ERROR_TMO) becomes
        /// <see cref="LocalVisaTimeout"/>; anything else is an I/O failure.
        /// </summary>
        private LocalVisaError IoError(Exception ex) =>
            ex is IOTimeoutException
            || ex is NativeVisaException { ErrorCode: NativeErrorCode.Timeout }
                ? new LocalVisaTimeout(_ioTimeout, ex)
                : new LocalVisaIoFailure(ex.Message, ex);

        public Result<Unit, LocalVisaError> EnableServiceRequests(Action<byte> onStatusByte)
        {
            // The CLR ServiceRequest event arms VISA's handler mechanism,
            // which NI-VISA rejects for service requests on USB sessions
            // (its add accessor throws). The queue mechanism is supported
            // across transports, so a dedicated pump waits on the queue.
            try
            {
                _session.EnableEvent(EventType.ServiceRequest);
            }
            catch (Exception ex)
            {
                return Result.Failure<Unit, LocalVisaError>(new LocalVisaIoFailure(ex.Message, ex));
            }

            var stop = new CancellationTokenSource();
            _srqPumpStop = stop;
            _ = Task.Factory.StartNew(
                () => PumpServiceRequests(onStatusByte, stop.Token),
                stop.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
            return Result.Success<Unit, LocalVisaError>(Unit.Value);
        }

        private void PumpServiceRequests(Action<byte> onStatusByte, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    _session.WaitOnEvent(EventType.ServiceRequest, SrqWaitSlice);
                }
                catch (IOTimeoutException)
                {
                    continue; // quiet slice; keep waiting
                }
                catch (NativeVisaException ex) when (ex.ErrorCode == NativeErrorCode.Timeout)
                {
                    continue;
                }
                catch (Exception)
                {
                    return; // session closed or faulted; the pump ends quietly
                }

                byte status;
                try
                {
                    status = (byte)_session.ReadStatusByte();
                }
                catch (Exception)
                {
                    // An SRQ whose status byte cannot be read must not kill the pump.
                    continue;
                }
                onStatusByte(status);
            }
        }

        private string ReadResponse() => _session.FormattedIO.ReadLine().TrimEnd('\r', '\n');

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_srqPumpStop is not null)
            {
                _srqPumpStop.Cancel();
                _srqPumpStop.Dispose();
                _srqPumpStop = null;
            }
            _session.Dispose();
        }
    }
}
