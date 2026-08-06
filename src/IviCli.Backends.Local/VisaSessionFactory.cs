using Ivi.Visa;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Production <see cref="IVisaSessionFactory"/> over the IVI Foundation
/// VISA.NET shared components. <see cref="GlobalResourceManager"/> locates an
/// installed vendor implementation at runtime; when none is registered, every
/// open returns <see cref="LocalVisaRuntimeMissing"/>.
/// </summary>
public sealed class VisaSessionFactory : IVisaSessionFactory
{
    /// <inheritdoc/>
    public Result<IVisaSessionHandle, LocalVisaError> Open(VisaResource resource, TimeSpan timeout)
    {
        var resourceString = VisaResourceFormatter.Format(resource);
        var timeoutMs = (int)timeout.TotalMilliseconds;
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
            messageBased.TimeoutMilliseconds = timeoutMs;
            return Result.Success<IVisaSessionHandle, LocalVisaError>(
                new VisaSessionHandle(messageBased)
            );
        }
        catch (Exception ex)
            when (ex is DllNotFoundException or FileNotFoundException or TypeInitializationException
            )
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing(
                    "no VISA implementation is registered; install a VISA runtime (e.g. NI-VISA or Keysight VISA)"
                )
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
        private readonly IMessageBasedSession _session;
        private EventHandler<VisaEventArgs>? _serviceRequestHandler;
        private bool _disposed;

        public VisaSessionHandle(IMessageBasedSession session)
        {
            _session = session;
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
                return Result.Failure<Unit, LocalVisaError>(new LocalVisaIoFailure(ex.Message, ex));
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
                return Result.Failure<string, LocalVisaError>(
                    new LocalVisaIoFailure(ex.Message, ex)
                );
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
                return Result.Failure<string, LocalVisaError>(
                    new LocalVisaIoFailure(ex.Message, ex)
                );
            }
        }

        public Result<Unit, LocalVisaError> EnableServiceRequests(Action<byte> onStatusByte)
        {
            EventHandler<VisaEventArgs> handler = (_, _) =>
            {
                StatusByteFlags status;
                try
                {
                    status = _session.ReadStatusByte();
                }
                catch (Exception)
                {
                    // An SRQ whose status byte cannot be read must not crash the VISA event thread.
                    return;
                }
                onStatusByte((byte)status);
            };
            try
            {
                _session.ServiceRequest += handler;
                _session.EnableEvent(EventType.ServiceRequest);
                _serviceRequestHandler = handler;
                return Result.Success<Unit, LocalVisaError>(Unit.Value);
            }
            catch (Exception ex)
            {
                Unhook(handler);
                return Result.Failure<Unit, LocalVisaError>(new LocalVisaIoFailure(ex.Message, ex));
            }
        }

        private string ReadResponse() => _session.FormattedIO.ReadLine().TrimEnd('\r', '\n');

        private void Unhook(EventHandler<VisaEventArgs> handler)
        {
            try
            {
                _session.ServiceRequest -= handler;
            }
            catch (Exception)
            { /* teardown is best-effort */
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_serviceRequestHandler is not null)
            {
                Unhook(_serviceRequestHandler);
                _serviceRequestHandler = null;
            }
            _session.Dispose();
        }
    }
}
