using Xunit;

namespace IviCli.TestKit;

/// <summary>
/// Marks an xUnit fact as requiring one or more external prerequisites
/// (Python, PyVISA, …). At test discovery time, missing prerequisites
/// set <see cref="FactAttribute.Skip"/> with a precise list of what is
/// missing, so the CI summary surfaces the reason rather than a silent
/// skip. Always also carry <see cref="TraitAttribute"/> for
/// <c>Category=Integration</c> on the same test so the runner's
/// <c>--filter</c> can include or exclude integration coverage.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresAttribute : FactAttribute
{
    /// <summary>
    /// Creates a Requires gate that runs the test only when every name
    /// in <paramref name="prerequisites"/> is satisfied by
    /// <see cref="PrereqProbe"/>.
    /// </summary>
    public RequiresAttribute(params string[] prerequisites)
    {
        ArgumentNullException.ThrowIfNull(prerequisites);
        var missing = PrereqProbe.Missing(prerequisites);
        if (missing.Length > 0)
        {
            Skip = $"missing prerequisite(s): {string.Join(", ", missing)}";
        }
    }
}
