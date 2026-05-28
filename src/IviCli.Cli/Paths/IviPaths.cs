namespace IviCli.Cli.Paths;

/// <summary>
/// Cross-platform path resolution for ivi-cli, following the XDG-style
/// conventions declared in PRD §8.1 and ADR 0011 §3.
/// </summary>
public static class IviPaths
{
    private const string AppFolderName = "ivi-cli";
    private const string ConfigFileName = "config.toml";
    private const string LogDirectoryName = "logs";
    private const string ServersDirectoryName = "servers";
    private const string AuthDirectoryName = "auth";
    private const string AuditDirectoryName = "audit";
    private const string PluginsDirectoryName = "plugins";

    private const string ConfigOverrideEnv = "IVICLI_CONFIG";
    private const string LogDirOverrideEnv = "IVICLI_LOG_DIR";
    private const string ServerStateDirOverrideEnv = "IVICLI_SERVER_STATE_DIR";
    private const string AuthDirOverrideEnv = "IVICLI_AUTH_DIR";
    private const string AuditDirOverrideEnv = "IVICLI_AUDIT_DIR";
    private const string PluginsDirOverrideEnv = "IVICLI_PLUGINS_DIR";

    /// <summary>
    /// Returns the absolute path to <c>config.toml</c>, respecting the
    /// <c>IVICLI_CONFIG</c> environment-variable override.
    /// </summary>
    public static string ResolveConfigPath()
    {
        var overrideValue = Environment.GetEnvironmentVariable(ConfigOverrideEnv);
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        var configRoot = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config"
                    )
            );

        return Path.Combine(configRoot, AppFolderName, ConfigFileName);
    }

    /// <summary>
    /// Returns the absolute path to the log directory, respecting the
    /// <c>IVICLI_LOG_DIR</c> environment-variable override.
    /// </summary>
    public static string ResolveLogDirectory()
    {
        var overrideValue = Environment.GetEnvironmentVariable(LogDirOverrideEnv);
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName,
                LogDirectoryName
            );
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Logs",
                AppFolderName
            );
        }

        // Linux / other Unix
        var stateRoot =
            Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state"
            );
        return Path.Combine(stateRoot, AppFolderName, LogDirectoryName);
    }

    /// <summary>
    /// Returns the absolute path to the per-server runtime state directory
    /// where PID files live, respecting the
    /// <c>IVICLI_SERVER_STATE_DIR</c> environment-variable override.
    /// </summary>
    public static string ResolveServerStateDirectory()
    {
        var overrideValue = Environment.GetEnvironmentVariable(ServerStateDirOverrideEnv);
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName,
                ServersDirectoryName
            );
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                AppFolderName,
                ServersDirectoryName
            );
        }

        // Linux / other Unix
        var stateRoot =
            Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state"
            );
        return Path.Combine(stateRoot, AppFolderName, ServersDirectoryName);
    }

    /// <summary>
    /// Returns the absolute path to the API authentication directory
    /// (ADR 0036). Hosts the <c>api-tokens.toml</c> file the
    /// Management API consults. Honours the
    /// <c>IVICLI_AUTH_DIR</c> environment-variable override.
    /// </summary>
    public static string ResolveAuthDirectory()
    {
        var overrideValue = Environment.GetEnvironmentVariable(AuthDirOverrideEnv);
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName,
                AuthDirectoryName
            );
        }

        // Linux / macOS: co-located with config.toml under
        // $XDG_CONFIG_HOME/ivi-cli/auth so users see all CLI state in
        // one tree.
        var configRoot =
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config"
            );
        return Path.Combine(configRoot, AppFolderName, AuthDirectoryName);
    }

    /// <summary>
    /// Returns the absolute path to the audit-log directory
    /// (ADR 0043). Hosts the append-only <c>audit.ndjson</c> file
    /// the security middleware writes to. Honours the
    /// <c>IVICLI_AUDIT_DIR</c> environment-variable override.
    /// </summary>
    public static string ResolveAuditDirectory()
    {
        var overrideValue = Environment.GetEnvironmentVariable(AuditDirOverrideEnv);
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName,
                AuditDirectoryName
            );
        }

        var configRoot =
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config"
            );
        return Path.Combine(configRoot, AppFolderName, AuditDirectoryName);
    }

    /// <summary>
    /// Returns the absolute path to the plugins directory
    /// (ADR 0013). Hosts subdirectories named after each plugin;
    /// each contains a <c>plugin.toml</c> manifest + the plugin DLL.
    /// Honours the <c>IVICLI_PLUGINS_DIR</c> environment-variable
    /// override.
    /// </summary>
    public static string ResolvePluginsDirectory()
    {
        var overrideValue = Environment.GetEnvironmentVariable(PluginsDirOverrideEnv);
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return overrideValue;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName,
                PluginsDirectoryName
            );
        }

        var configRoot =
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config"
            );
        return Path.Combine(configRoot, AppFolderName, PluginsDirectoryName);
    }
}
