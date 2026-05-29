using System.Reflection;
using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Application.Mock;
using Shouldly;

namespace IviCli.Application.Tests.Architecture;

/// <summary>
/// Drift guard for ADR 0043 audit wiring. Any command handler in
/// <c>IviCli.Application</c> that persists operator-managed state
/// (i.e. its constructor depends on <see cref="IConfigStore"/> or
/// <see cref="IScenarioStore"/>) MUST also depend on
/// <see cref="IAuditLog"/> so the corresponding
/// <see cref="ConfigMutated"/> event reliably reaches the audit
/// sink. Future handlers added without audit injection fail this
/// test instead of silently swallowing the audit emission.
/// </summary>
public sealed class AuditWiringTests
{
    [Fact]
    public void All_mutating_command_handlers_depend_on_IAuditLog()
    {
        var asm = typeof(ConfigMutated).Assembly;
        var mutating = asm.GetTypes()
            .Where(t =>
                t.IsClass
                && !t.IsAbstract
                && t.Name.EndsWith("CommandHandler", StringComparison.Ordinal)
                && IsMutator(t.Name)
                && CtorDependsOn(t, typeof(IConfigStore), typeof(IScenarioStore))
            )
            .ToArray();

        // Sanity: the known 10 wired in Batch U should be discovered.
        mutating.Length.ShouldBeGreaterThanOrEqualTo(10);

        foreach (var t in mutating)
        {
            var hasAudit = CtorDependsOn(t, typeof(IAuditLog));
            hasAudit.ShouldBeTrue(
                $"{t.Name} mutates persistent state via SaveAsync but does "
                    + "not depend on IAuditLog. Add IAuditLog + IAuditSubject "
                    + "ctor params and emit ConfigMutated on success (ADR 0043)."
            );
        }
    }

    private static bool IsMutator(string typeName) =>
        typeName.StartsWith("Add", StringComparison.Ordinal)
        || typeName.StartsWith("Remove", StringComparison.Ordinal)
        || typeName.StartsWith("Create", StringComparison.Ordinal)
        || typeName.StartsWith("Import", StringComparison.Ordinal);

    private static bool CtorDependsOn(Type t, params Type[] anyOf)
    {
        foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var p in ctor.GetParameters())
            {
                if (anyOf.Any(target => target.IsAssignableFrom(p.ParameterType)))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
