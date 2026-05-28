namespace IviCli.Plugin;

/// <summary>
/// The contract every ivi-cli plugin assembly exports exactly once
/// (ADR 0013). The host discovers types implementing this interface
/// during composition-root startup, instantiates one per plugin
/// directory via its parameterless constructor, and calls
/// <see cref="Register"/> so the plugin can publish its
/// implementations to the running container.
/// </summary>
public interface IIviPlugin
{
    /// <summary>Stable plugin identifier matching the directory name.</summary>
    string Name { get; }

    /// <summary>Plugin version string, surfaced in <c>ivicli plugins list</c> (future verb).</summary>
    string Version { get; }

    /// <summary>
    /// The ivi-cli plugin API version this plugin was compiled against.
    /// Must equal <see cref="HostApiVersion.Current"/>; mismatches are
    /// rejected at load time.
    /// </summary>
    int TargetApiVersion { get; }

    /// <summary>
    /// Registers the plugin's services (backends, future gateway servers)
    /// with the host. Invoked once at composition-root startup.
    /// </summary>
    void Register(IPluginServices services);
}

/// <summary>
/// Constant identifying the plugin ABI shipped by the current host.
/// Plugins targeting a different value are refused at load time.
/// </summary>
public static class HostApiVersion
{
    /// <summary>The current plugin ABI version (incremented on breaking changes).</summary>
    public const int Current = 1;
}
