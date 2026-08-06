using System.Reflection;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Production <see cref="IVisaSessionFactory"/>. Loads the IVI VISA
/// shared component (typically <c>Ivi.Visa.dll</c> installed by NI-VISA
/// or Keysight VISA) at first use via reflection. When the runtime is
/// not installed, every open call returns
/// <see cref="LocalVisaRuntimeMissing"/>.
/// </summary>
/// <remarks>
/// The <c>Ivi.Visa</c> assembly resolves from the application directory
/// (the <c>IviFoundation.Visa</c> package). The reflective binding
/// tolerates slight VISA.NET API drift across shared-component versions.
/// </remarks>
public sealed class ReflectionVisaSessionFactory : IVisaSessionFactory
{
    private const string SharedAssemblyName = "Ivi.Visa";
    private const string GlobalResourceManagerTypeName = "Ivi.Visa.GlobalResourceManager";
    private const string MessageBasedSessionTypeName = "Ivi.Visa.IMessageBasedSession";

    private readonly Lazy<RuntimeBindings?> _bindings;

    /// <summary>Creates a factory that defers VISA assembly loading until first use.</summary>
    public ReflectionVisaSessionFactory()
    {
        _bindings = new Lazy<RuntimeBindings?>(TryLoadBindings, isThreadSafe: true);
    }

    /// <inheritdoc/>
    public Result<IVisaSessionHandle, LocalVisaError> Open(VisaResource resource, TimeSpan timeout)
    {
        if (_bindings.Value is not { } bindings)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaRuntimeMissing(
                    $"could not load assembly '{SharedAssemblyName}'. Install NI-VISA or Keysight VISA so '{SharedAssemblyName}.dll' is on the assembly probe path."
                )
            );
        }

        var resourceString = VisaResourceFormatter.Format(resource);
        try
        {
            var session = bindings.OpenSession(resourceString, (int)timeout.TotalMilliseconds);
            return Result.Success<IVisaSessionHandle, LocalVisaError>(
                new ReflectionVisaSessionHandle(session, bindings)
            );
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return Result.Failure<IVisaSessionHandle, LocalVisaError>(
                new LocalVisaOpenFailure(
                    resourceString,
                    ex.InnerException.Message,
                    ex.InnerException
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

    private static RuntimeBindings? TryLoadBindings()
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(SharedAssemblyName);
        }
        catch (Exception)
        {
            return null;
        }

        var grm = assembly.GetType(GlobalResourceManagerTypeName);
        var messageBased = assembly.GetType(MessageBasedSessionTypeName);
        if (grm is null || messageBased is null)
        {
            return null;
        }

        var openMethod = grm.GetMethod(
            "Open",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(int), typeof(int) },
            modifiers: null
        );
        if (openMethod is null)
        {
            return null;
        }

        var writeMethod = messageBased.GetMethod(
            "RawIO",
            BindingFlags.Public | BindingFlags.Instance
        );
        // The IVI API exposes Write(string)/Query(string)/ReadString() on
        // IMessageBasedSession. Their reflective handles live on the
        // bindings record below; we resolve them lazily per call so that
        // alternative VISA bindings with slight API drift still work.
        return new RuntimeBindings(grm, messageBased, openMethod);
    }

    private sealed record RuntimeBindings(
        Type GrmType,
        Type MessageBasedType,
        MethodInfo OpenMethod
    )
    {
        public object OpenSession(string resource, int timeoutMs)
        {
            var session =
                OpenMethod.Invoke(null, new object?[] { resource, 0, timeoutMs })
                ?? throw new InvalidOperationException("VISA GRM.Open returned null.");
            return session;
        }
    }

    private sealed class ReflectionVisaSessionHandle : IVisaSessionHandle
    {
        private readonly object _session;
        private readonly RuntimeBindings _bindings;
        private bool _disposed;

        public ReflectionVisaSessionHandle(object session, RuntimeBindings bindings)
        {
            _session = session;
            _bindings = bindings;
        }

        public Result<Unit, LocalVisaError> Write(string text)
        {
            try
            {
                Invoke("Write", new object[] { text });
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
                Invoke("Write", new object[] { text });
                var response = Invoke("ReadString", Array.Empty<object>());
                return Result.Success<string, LocalVisaError>(response?.ToString() ?? string.Empty);
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
                var response = Invoke("ReadString", Array.Empty<object>());
                return Result.Success<string, LocalVisaError>(response?.ToString() ?? string.Empty);
            }
            catch (Exception ex)
            {
                return Result.Failure<string, LocalVisaError>(
                    new LocalVisaIoFailure(ex.Message, ex)
                );
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_session is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // Swallow on dispose path; we can't surface from here.
                }
            }
        }

        private object? Invoke(string memberName, object[] args)
        {
            var method = _bindings.MessageBasedType.GetMethod(
                memberName,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: args.Select(a => a.GetType()).ToArray(),
                modifiers: null
            );
            if (method is null)
            {
                throw new MissingMethodException(_bindings.MessageBasedType.FullName, memberName);
            }
            return method.Invoke(_session, args);
        }
    }
}

/// <summary>Formats <see cref="VisaResource"/> back to a VISA resource string.</summary>
public static class VisaResourceFormatter
{
    /// <summary>Formats <paramref name="resource"/> to its canonical VISA resource string form.</summary>
    public static string Format(VisaResource resource) =>
        resource switch
        {
            VisaResource.Tcpip t => $"TCPIP{t.Board}::{t.Host}::{t.LanDevice}::INSTR",
            VisaResource.Usb u when u.InterfaceNumber is null =>
                $"USB{u.Board}::{u.VendorId}::{u.ProductId}::{u.SerialNumber}::INSTR",
            VisaResource.Usb u =>
                $"USB{u.Board}::{u.VendorId}::{u.ProductId}::{u.SerialNumber}::{u.InterfaceNumber}::INSTR",
            VisaResource.Gpib g when g.SecondaryAddress is null =>
                $"GPIB{g.Board}::{g.PrimaryAddress}::INSTR",
            VisaResource.Gpib g =>
                $"GPIB{g.Board}::{g.PrimaryAddress}::{g.SecondaryAddress}::INSTR",
            _ => throw new NotSupportedException(
                $"Unsupported VisaResource variant for VISA formatting: {resource.GetType().Name}"
            ),
        };
}
