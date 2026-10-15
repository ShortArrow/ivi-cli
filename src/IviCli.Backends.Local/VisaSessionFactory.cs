using Ivi.Visa;
using IviCli.Domain;
using IviCli.Domain.Devices;
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
        VisaSessionOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!VisaRuntime.IsInstalled)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing(VisaRuntime.MissingDetail)
            );
        }
        var resourceString = VisaResourceFormatter.Format(resource);
        var timeoutMs = (int)options.OpenTimeout.TotalMilliseconds;
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
            if (SetUp(messageBased, options) is Result<Unit, LocalVisaError>.Error refused)
            {
                return Result.Failure<IVisaSessionHandle, LocalVisaError>(refused.Err);
            }
            return Result.Success<IVisaSessionHandle, LocalVisaError>(
                new VisaSessionHandle(messageBased, options)
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

    /// <summary>
    /// Sets up a newly opened <paramref name="session"/> as
    /// <paramref name="options"/> say: the I/O timeout, the serial line, and
    /// the read terminator (ADR 0055 §4, §5). A session that fails to set
    /// up is disposed, refused or thrown, since a session left to the
    /// finalizer can end the process (#228).
    /// </summary>
    /// <returns>
    /// Success, or <see cref="LocalVisaSettingRefused"/> for a serial setting
    /// the driver refused.
    /// </returns>
    public static Result<Unit, LocalVisaError> SetUp(
        IMessageBasedSession session,
        VisaSessionOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            session.TimeoutMilliseconds = (int)options.IoTimeout.TotalMilliseconds;
            if (
                options.Serial is { } serial
                && session is ISerialSession serialSession
                && VisaSerialLine.Configure(serialSession, serial)
                    is Result<Unit, LocalVisaError>.Error refused
            )
            {
                session.Dispose();
                return refused;
            }
            if (options.ReadTermination is { } terminator)
            {
                session.TerminationCharacter = VisaTermination.Character(terminator);
                session.TerminationCharacterEnabled = true;
            }
            return Result.Success<Unit, LocalVisaError>(Unit.Value);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private sealed class VisaSessionHandle : IVisaSessionHandle
    {
        private static readonly TimeSpan SrqWaitSlice = TimeSpan.FromMilliseconds(500);

        private readonly IMessageBasedSession _session;
        private readonly TimeSpan _ioTimeout;
        private readonly Termination? _readTermination;
        private readonly Termination? _writeTermination;
        private CancellationTokenSource? _srqPumpStop;
        private bool _disposed;

        public VisaSessionHandle(IMessageBasedSession session, VisaSessionOptions options)
        {
            _session = session;
            _ioTimeout = options.IoTimeout;
            _readTermination = options.ReadTermination;
            _writeTermination = options.WriteTermination;
        }

        public Result<Unit, LocalVisaError> Write(string text)
        {
            try
            {
                WriteMessage(text);
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
                WriteMessage(text);
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

        private LocalVisaError IoError(Exception ex) => VisaIoErrors.From(ex, _ioTimeout);

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

        private void WriteMessage(string text)
        {
            if (_writeTermination is { } terminator)
            {
                _session.RawIO.Write(VisaTermination.Encode(text, terminator));
            }
            else
            {
                _session.FormattedIO.WriteLine(text);
            }
        }

        /// <summary>
        /// Reads one reply: up to the termination character when a read
        /// terminator is set, which the session ends each read at, and
        /// otherwise a line, with a trailing CR removed (ADR 0055 §5).
        /// </summary>
        private string ReadResponse() =>
            _readTermination is { } terminator
                ? VisaTermination.ReadMessage(() => _session.RawIO.Read(), terminator)
                : _session.FormattedIO.ReadLine().TrimEnd('\r', '\n');

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
