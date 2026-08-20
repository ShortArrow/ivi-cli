using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using IviCli.Api;
using IviCli.Api.Authentication;
using IviCli.Api.Tls;
using IviCli.Application.Auth;
using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Auth;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli api ...</c> subcommand tree (ADR 0034). The
/// Management API lives behind two verbs:
/// <list type="bullet">
/// <item><c>api start [--port 8080] [--bind 127.0.0.1]</c> — foreground listener.</item>
/// <item><c>api stop</c> — sends a Ctrl+C / SIGTERM to the running listener.</item>
/// </list>
/// </summary>
public static class ApiCommand
{
    /// <summary>The reserved <see cref="ServerName"/> used to track the API's PID file.</summary>
    public static readonly ServerName ReservedName = ServerName.From("ivi-management-api")
        is Result<ServerName, ServerNameError>.Ok ok
        ? ok.Value
        : throw new InvalidOperationException("ivi-management-api must be a valid ServerName");

    /// <summary>Default port for plaintext HTTP when <c>--port</c> is not supplied.</summary>
    public const int DefaultPort = 8080;

    /// <summary>Default port for HTTPS when <c>--port</c> is not supplied and TLS is enabled.</summary>
    public const int DefaultTlsPort = 8443;

    /// <summary>Default bind address (loopback only — non-loopback warns at startup).</summary>
    public const string DefaultBind = "127.0.0.1";

    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("api", "Manage the local Management API (HTTP JSON).");
        command.Subcommands.Add(BuildStart(services));
        command.Subcommands.Add(BuildStop(services));
        command.Subcommands.Add(ApiTokenCommand.Build(services));
        return command;
    }

    private static Command BuildStart(IServiceProvider services)
    {
        var portOpt = new Option<int?>("--port")
        {
            Description =
                $"TCP port to bind on (default {DefaultPort} HTTP, {DefaultTlsPort} HTTPS).",
        };
        var bindOpt = new Option<string>("--bind")
        {
            Description = $"Listen address (default {DefaultBind}).",
            DefaultValueFactory = _ => DefaultBind,
        };
        var allowAnonOpt = new Option<bool>("--allow-anonymous")
        {
            Description =
                "Allow requests without an API token. Required when binding non-loopback "
                + "with no tokens configured (ADR 0036).",
        };
        var tlsOpt = new Option<bool>("--tls")
        {
            Description = "Serve HTTPS instead of HTTP (ADR 0039).",
        };
        var tlsCertOpt = new Option<string?>("--tls-cert")
        {
            Description = "Path to the server certificate (PFX or PEM).",
        };
        var tlsKeyOpt = new Option<string?>("--tls-key")
        {
            Description = "Path to the PEM private key (when --tls-cert is a PEM file).",
        };
        var tlsPasswordEnvOpt = new Option<string?>("--tls-password-env")
        {
            Description = "Environment variable holding the PFX password.",
        };
        var tlsSelfSignedOpt = new Option<bool>("--tls-self-signed")
        {
            Description = "Generate an ephemeral self-signed cert (dev only).",
        };
        var tlsClientRequiredOpt = new Option<bool>("--tls-client-required")
        {
            Description = "Require client certificate (mTLS).",
        };
        var tlsClientCaOpt = new Option<string?>("--tls-client-ca")
        {
            Description =
                "Path to PEM bundle of trusted client CAs (required with --tls-client-required).",
        };

        var cmd = new Command("start", "Start the Management API listener in the foreground.");
        cmd.Options.Add(portOpt);
        cmd.Options.Add(bindOpt);
        cmd.Options.Add(allowAnonOpt);
        cmd.Options.Add(tlsOpt);
        cmd.Options.Add(tlsCertOpt);
        cmd.Options.Add(tlsKeyOpt);
        cmd.Options.Add(tlsPasswordEnvOpt);
        cmd.Options.Add(tlsSelfSignedOpt);
        cmd.Options.Add(tlsClientRequiredOpt);
        cmd.Options.Add(tlsClientCaOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var bindRaw = parseResult.GetValue(bindOpt) ?? DefaultBind;
                var allowAnon = parseResult.GetValue(allowAnonOpt);
                if (!IPAddress.TryParse(bindRaw, out var bindAddr))
                {
                    Console.Error.WriteLine($"error: invalid --bind value '{bindRaw}'.");
                    return ExitCodeMapper.UsageError;
                }
                var isLoopback = IPAddress.IsLoopback(bindAddr);

                // Resolve the effective TlsConfig from CLI flags + config
                // file. CLI flags fully override the config file when
                // --tls / --tls-cert / --tls-self-signed are present.
                var configStore = services.GetRequiredService<IConfigStore>();
                var configLoad = await configStore.LoadAsync(ct);
                var configDoc = configLoad
                    is Result<ConfigDocument, ConfigStoreError>.Ok { Value: var cfg }
                    ? cfg
                    : ConfigDocument.Empty;
                var tlsResult = BuildEffectiveTlsConfig(
                    parseResult,
                    configDoc.Api.Tls,
                    tlsOpt,
                    tlsCertOpt,
                    tlsKeyOpt,
                    tlsPasswordEnvOpt,
                    tlsSelfSignedOpt,
                    tlsClientRequiredOpt,
                    tlsClientCaOpt
                );
                if (tlsResult is not Result<TlsConfig, TlsConfigError>.Ok { Value: var tlsCfg })
                {
                    var err = ((Result<TlsConfig, TlsConfigError>.Error)tlsResult).Err;
                    Console.Error.WriteLine($"error: {err.Message}");
                    return ExitCodeMapper.UsageError;
                }

                var port =
                    parseResult.GetValue(portOpt)
                    ?? (tlsCfg.Enabled ? DefaultTlsPort : DefaultPort);

                // Non-loopback gate (ADR 0036 §4): require at least one
                // configured token unless the operator opts out explicitly.
                var tokenStore = services.GetRequiredService<IApiTokenStore>();
                var tokenLoad = await tokenStore.LoadAsync(ct);
                var tokensConfigured =
                    tokenLoad is Result<ApiTokenDocument, ApiTokenStoreError>.Ok { Value: var doc }
                    && !doc.Tokens.IsDefaultOrEmpty;
                if (!isLoopback && !tokensConfigured && !allowAnon)
                {
                    Console.Error.WriteLine(
                        "error: non-loopback bind requires at least one API token "
                            + "(create one with 'ivicli api token create') "
                            + "or pass --allow-anonymous to opt out (ADR 0036)."
                    );
                    return ExitCodeMapper.UsageError;
                }

                // The middleware reads these flags via the API builder's
                // forwarded ApiAuthenticationOptions registration. We
                // mutate the singleton in place so the API process sees
                // the runtime values without a rebuild.
                var authOptions = services.GetRequiredService<ApiAuthenticationOptions>();
                authOptions.IsLoopback = isLoopback;
                authOptions.AllowAnonymous = (isLoopback && !tokensConfigured) || allowAnon;

                var registry = services.GetRequiredService<IServerProcessRegistry>();
                var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("IviCli.Cli.Commands.ApiCommand");

                TlsCertificateBundle? bundle = null;
                RotatingTlsCertificate? rotation = null;
                if (tlsCfg.Enabled)
                {
                    var loaded = TlsCertificateLoader.Load(tlsCfg);
                    if (
                        loaded is not Result<TlsCertificateBundle, TlsLoadError>.Ok { Value: var b }
                    )
                    {
                        var loadErr = (
                            (Result<TlsCertificateBundle, TlsLoadError>.Error)loaded
                        ).Err;
                        Console.Error.WriteLine(
                            $"error: {IviCli.Application.Logging.IviErrorMessages.Render(loadErr)}"
                        );
                        return ExitCodeMapper.DeviceError;
                    }
                    bundle = b;
                    rotation = new RotatingTlsCertificate(
                        b,
                        tlsCfg,
                        logger,
                        services.GetRequiredService<IviCli.Application.Audit.IAuditLog>()
                    );
                    if (b.SelfSigned)
                    {
                        logger.LogWarning(
                            "TLS enabled with a self-signed certificate — DO NOT use --tls-self-signed in production."
                        );
                    }
                }

                var pid = Environment.ProcessId;
                _ = await registry.WriteAsync(ReservedName, pid, DateTimeOffset.UtcNow, ct);

                var scheme = tlsCfg.Enabled ? "https" : "http";
                logger.LogInformation(
                    "Management API listening on {Scheme}://{Bind}:{Port}",
                    scheme,
                    bindAddr,
                    port
                );

                try
                {
                    var app = IviCliApiBuilder.Build(
                        services,
                        bindAddr,
                        port,
                        bundle,
                        rotation is null ? null : () => rotation.Current
                    );
                    if (rotation is { CanRotate: true })
                    {
                        // Cert hot-reload (ADR 0039): rotated files are
                        // picked up on the next handshake, no restart.
                        _ = Task.Run(() => rotation.RunAsync(ct), ct);
                    }
                    await ((IHost)app).RunAsync(ct);
                    return ExitCodeMapper.Success;
                }
                catch (OperationCanceledException)
                {
                    return ExitCodeMapper.Success;
                }
                finally
                {
                    _ = await registry.DeleteAsync(ReservedName, CancellationToken.None);
                }
            }
        );
        return cmd;
    }

    private static Result<TlsConfig, TlsConfigError> BuildEffectiveTlsConfig(
        System.CommandLine.ParseResult parseResult,
        TlsConfig fromConfig,
        Option<bool> tlsOpt,
        Option<string?> tlsCertOpt,
        Option<string?> tlsKeyOpt,
        Option<string?> tlsPasswordEnvOpt,
        Option<bool> tlsSelfSignedOpt,
        Option<bool> tlsClientRequiredOpt,
        Option<string?> tlsClientCaOpt
    )
    {
        // When any --tls* flag is present, the CLI fully replaces the
        // config-file TLS section (single source of truth for one run).
        // Otherwise the config file wins.
        var cliTls = parseResult.GetValue(tlsOpt);
        var cliCert = parseResult.GetValue(tlsCertOpt);
        var cliKey = parseResult.GetValue(tlsKeyOpt);
        var cliPasswordEnv = parseResult.GetValue(tlsPasswordEnvOpt);
        var cliSelfSigned = parseResult.GetValue(tlsSelfSignedOpt);
        var cliClientRequired = parseResult.GetValue(tlsClientRequiredOpt);
        var cliClientCa = parseResult.GetValue(tlsClientCaOpt);

        var anyCliTls =
            cliTls
            || cliCert is not null
            || cliKey is not null
            || cliPasswordEnv is not null
            || cliSelfSigned
            || cliClientRequired
            || cliClientCa is not null;
        if (!anyCliTls)
        {
            return Result.Success<TlsConfig, TlsConfigError>(fromConfig);
        }

        var enabled = cliTls || cliCert is not null || cliSelfSigned;
        return TlsConfig.From(
            enabled,
            cliCert,
            cliKey,
            cliPasswordEnv,
            cliSelfSigned,
            cliClientRequired,
            cliClientCa
        );
    }

    private static Command BuildStop(IServiceProvider services)
    {
        var cmd = new Command("stop", "Stop the running Management API listener.");

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var registry = services.GetRequiredService<IServerProcessRegistry>();
                var entryResult = await registry.ReadAsync(ReservedName, ct);
                if (
                    entryResult
                    is not Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok
                    {
                        Value: { } entry,
                    }
                )
                {
                    Console.Error.WriteLine("error: Management API is not running.");
                    return ExitCodeMapper.DeviceError;
                }
                try
                {
                    using var process = Process.GetProcessById(entry.ProcessId);
                    SignalGracefulExit(process);
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    _ = await registry.DeleteAsync(ReservedName, ct);
                    Console.WriteLine("Management API stopped.");
                    return ExitCodeMapper.Success;
                }
                catch (ArgumentException)
                {
                    // Process is already gone — clean up the stale PID file.
                    _ = await registry.DeleteAsync(ReservedName, ct);
                    Console.WriteLine(
                        "Management API was not running (cleaned up stale PID file)."
                    );
                    return ExitCodeMapper.Success;
                }
            }
        );
        return cmd;
    }

    private static void SignalGracefulExit(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                process.CloseMainWindow();
            }
            catch
            {
                // No console window — fall through to Kill.
            }
        }
        else
        {
            // POSIX: send SIGTERM via the process group. We don't have a
            // managed cross-platform signal API, so fall back to Kill which
            // sends SIGKILL on POSIX. The 5 s grace-window above gives the
            // process a chance to exit on its own first if a hosting shell
            // forwards Ctrl+C.
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch
            {
                // Best-effort.
            }
        }
    }
}
