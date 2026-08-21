using System.Diagnostics.CodeAnalysis;

namespace IviCli.Infrastructure.Plugins;

/// <summary>
/// Feature switch gating the reflection-based plugin loader (ADR 0013 /
/// issue #15). Loading arbitrary assemblies cannot survive trimming or
/// NativeAOT, so an AOT publish sets the
/// <c>IviCli.Plugins.IsSupported</c> runtime host switch to
/// <see langword="false"/> and the trimmer removes the loader with the
/// guarded branch; a JIT build leaves the switch unset and plugins work
/// as before.
/// </summary>
public static class PluginSupport
{
    /// <summary>True unless the host configuration turned plugins off.</summary>
    [FeatureSwitchDefinition("IviCli.Plugins.IsSupported")]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL4000",
        Justification = "Defaulting to true is safe because every trimmed/AOT "
            + "publish flavor in this repository pins the switch to false via "
            + "RuntimeHostConfigurationOption (Trim=true), so the trimmer sees "
            + "a constant and removes the guarded loader; a JIT build never trims."
    )]
    public static bool IsSupported =>
        !AppContext.TryGetSwitch("IviCli.Plugins.IsSupported", out var supported) || supported;
}
