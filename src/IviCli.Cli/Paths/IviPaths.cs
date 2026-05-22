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

    private const string ConfigOverrideEnv = "IVICLI_CONFIG";
    private const string LogDirOverrideEnv = "IVICLI_LOG_DIR";

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
}
