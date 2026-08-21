using System.Collections.Immutable;
using System.Text;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using Tomlyn;
using Tomlyn.Model;

namespace IviCli.Infrastructure.Mock;

/// <summary>
/// Pure parser/serializer for a single scenario TOML file. Mirrors
/// <c>TomlConfigParser</c>'s split-from-IO design (ADR 0023 §5 Impureim
/// Sandwich).
///
/// Two TOML schemas are recognised on read (issue #26 §15):
///
/// <list type="bullet">
/// <item><description>
/// <b>v0.2.0 multi-scene</b>: top-level <c>initial_scene</c> string +
/// <c>[[scenes]]</c> tables that carry a <c>name</c> field and an
/// optional <c>[[scenes.rules]]</c> sub-array. When present, this
/// shape is preserved exactly on round-trip.
/// </description></item>
/// <item><description>
/// <b>v0.1.x flat</b>: <c>[[scenes]]</c> tables that carry a
/// <c>match</c> field (no <c>name</c>, no nested rules). Every flat
/// entry is wrapped as a rule inside a synthetic <c>default</c>
/// scene; <c>InitialScene</c> defaults to <c>default</c>. This keeps
/// pre-v0.2.0 scenario files loadable without migration.
/// </description></item>
/// </list>
///
/// Both shapes accept the optional <c>[quirks]</c> table, which names
/// the firmware misbehaviour the mock reproduces (issue #115).
///
/// Serialisation always emits the v0.2.0 multi-scene shape, *except*
/// for scenarios that have a single scene named <c>default</c> with
/// no transitions — those are written back in the v0.1.x flat form to
/// minimise diff noise for users who have not adopted the FSM
/// features yet.
/// </summary>
public static class TomlScenarioParser
{
    private const string IdnField = "idn";
    private const string InitialSceneField = "initial_scene";
    private const string ScenesArray = "scenes";
    private const string NameField = "name";
    private const string RulesArray = "rules";
    private const string MatchField = "match";
    private const string RespondField = "respond";
    private const string AckField = "ack";
    private const string FailField = "fail";
    private const string FailDetailField = "fail_detail";
    private const string TransitionToField = "transition_to";
    private const string SrqField = "srq";
    private const string QuirksTable = "quirks";
    private const string SrqNotifyWedgeAfterField = "srq_notify_wedge_after";

    /// <summary>
    /// Parses a TOML document into a <see cref="MockScenario"/>. The scenario's
    /// name is supplied separately because the on-disk filename is the
    /// authoritative source for the name (ADR 0026 §2).
    /// </summary>
    public static Result<MockScenario, ScenarioStoreError> Parse(ScenarioName name, string toml)
    {
        TomlTable model;
        try
        {
            model =
                TomlSerializer.Deserialize<TomlTable>(toml, TomlModelContext.Default)
                ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            return Fail($"TOML syntax error: {ex.Message}");
        }

        string? idnDefault = null;
        if (model.TryGetValue(IdnField, out var idnValue))
        {
            if (idnValue is not string idnString)
            {
                return Fail("expected `idn` to be a string");
            }
            idnDefault = idnString;
        }

        string? initialSceneRaw = null;
        if (model.TryGetValue(InitialSceneField, out var initValue))
        {
            if (initValue is not string initString)
            {
                return Fail($"expected `{InitialSceneField}` to be a string");
            }
            initialSceneRaw = initString;
        }

        var quirksResult = ParseQuirks(model);
        if (quirksResult is not Result<MockQuirks?, ScenarioStoreError>.Ok quirksOk)
        {
            return Result.Failure<MockScenario, ScenarioStoreError>(
                ((Result<MockQuirks?, ScenarioStoreError>.Error)quirksResult).Err
            );
        }

        if (!model.TryGetValue(ScenesArray, out var scenesValue))
        {
            return WithQuirks(
                Result.Success<MockScenario, ScenarioStoreError>(
                    MockScenario.SingleScene(name, idnDefault, ImmutableArray<MockRule>.Empty)
                ),
                quirksOk.Value
            );
        }
        if (scenesValue is not TomlTableArray sceneTables)
        {
            return Fail("expected `[[scenes]]` to be an array of tables");
        }

        var isV02Schema =
            sceneTables.Count > 0
            && sceneTables[0].ContainsKey(NameField)
            && !sceneTables[0].ContainsKey(MatchField);
        return WithQuirks(
            isV02Schema
                ? ParseMultiScene(name, idnDefault, initialSceneRaw, sceneTables)
                : ParseFlatRules(name, idnDefault, sceneTables),
            quirksOk.Value
        );
    }

    private static Result<MockScenario, ScenarioStoreError> WithQuirks(
        Result<MockScenario, ScenarioStoreError> scenario,
        MockQuirks? quirks
    ) =>
        scenario is Result<MockScenario, ScenarioStoreError>.Ok ok
            ? Result.Success<MockScenario, ScenarioStoreError>(ok.Value with { Quirks = quirks })
            : scenario;

    /// <summary>
    /// Reads the optional <c>[quirks]</c> table. A missing table and a
    /// table that names no quirk both yield <see langword="null"/>, so
    /// serialising either back omits the table entirely.
    /// </summary>
    private static Result<MockQuirks?, ScenarioStoreError> ParseQuirks(TomlTable model)
    {
        if (!model.TryGetValue(QuirksTable, out var quirksValue))
        {
            return Result.Success<MockQuirks?, ScenarioStoreError>(null);
        }
        if (quirksValue is not TomlTable quirksTable)
        {
            return FailQuirks($"expected `[{QuirksTable}]` to be a table");
        }

        int? srqNotifyWedgeAfter = null;
        if (quirksTable.TryGetValue(SrqNotifyWedgeAfterField, out var wedgeValue))
        {
            if (wedgeValue is not long wedgeLong)
            {
                return FailQuirks($"expected `{SrqNotifyWedgeAfterField}` to be an integer");
            }
            if (wedgeLong is < 0 or > int.MaxValue)
            {
                return FailQuirks(
                    $"`{SrqNotifyWedgeAfterField}` must be zero or more (0 wedges the stream before the first delivery)"
                );
            }
            srqNotifyWedgeAfter = (int)wedgeLong;
        }

        var quirks = new MockQuirks(srqNotifyWedgeAfter);
        return Result.Success<MockQuirks?, ScenarioStoreError>(quirks.IsEmpty ? null : quirks);
    }

    /// <summary>Serializes a scenario back to TOML.</summary>
    public static string Serialize(MockScenario scenario)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var builder = new StringBuilder();

        if (scenario.IdnDefault is { } idn)
        {
            builder.AppendLine(inv, $"idn = \"{Escape(idn)}\"");
        }

        // v0.1.x flat-shape preservation: a scenario with a single
        // `default` scene whose rules carry no transitions is the
        // exact shape v0.1.x users authored — emit the flat form so
        // existing files round-trip unchanged.
        var canEmitFlat =
            scenario.Scenes.Length == 1
            && scenario.Scenes[0].Name == SceneName.DefaultScene()
            && scenario.InitialScene == SceneName.DefaultScene()
            && scenario.Scenes[0].Rules.All(r => r.Action.Transition is null);
        if (canEmitFlat)
        {
            if (scenario.IdnDefault is not null)
            {
                builder.AppendLine();
            }
            AppendQuirks(builder, scenario.Quirks, inv);
            foreach (var rule in scenario.Scenes[0].Rules)
            {
                builder.AppendLine("[[scenes]]");
                AppendRuleBody(builder, rule, inv);
                builder.AppendLine();
            }
            return builder.ToString();
        }

        // v0.2.0 multi-scene shape.
        builder.AppendLine(inv, $"initial_scene = \"{Escape(scenario.InitialScene.Value)}\"");
        builder.AppendLine();
        AppendQuirks(builder, scenario.Quirks, inv);

        foreach (var scene in scenario.Scenes)
        {
            builder.AppendLine("[[scenes]]");
            builder.AppendLine(inv, $"name = \"{Escape(scene.Name.Value)}\"");
            builder.AppendLine();
            foreach (var rule in scene.Rules)
            {
                builder.AppendLine("[[scenes.rules]]");
                AppendRuleBody(builder, rule, inv);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Emits the <c>[quirks]</c> table, or nothing at all when the
    /// scenario names no quirk — scenario files written before quirks
    /// existed must survive a load-save cycle byte for byte.
    /// </summary>
    private static void AppendQuirks(
        StringBuilder builder,
        MockQuirks? quirks,
        System.Globalization.CultureInfo inv
    )
    {
        if (quirks is not { IsEmpty: false })
        {
            return;
        }
        builder.AppendLine(inv, $"[{QuirksTable}]");
        if (quirks.SrqNotifyWedgeAfter is { } wedgeAfter)
        {
            builder.AppendLine(inv, $"{SrqNotifyWedgeAfterField} = {wedgeAfter}");
        }
        builder.AppendLine();
    }

    private static void AppendRuleBody(
        StringBuilder builder,
        MockRule rule,
        System.Globalization.CultureInfo inv
    )
    {
        builder.AppendLine(inv, $"match = \"{Escape(rule.Match)}\"");
        switch (rule.Action)
        {
            case RuleAction.Respond r:
                builder.AppendLine(inv, $"respond = \"{Escape(r.Text)}\"");
                break;
            case RuleAction.Ack:
                builder.AppendLine("ack = true");
                break;
            case RuleAction.Fail f:
                builder.AppendLine(inv, $"fail = \"{Escape(f.Variant)}\"");
                if (f.Detail is { } detail)
                {
                    builder.AppendLine(inv, $"fail_detail = \"{Escape(detail)}\"");
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"unknown RuleAction variant: {rule.Action.GetType().Name}"
                );
        }
        if (rule.Action.Transition is { } target)
        {
            builder.AppendLine(inv, $"transition_to = \"{Escape(target.Value)}\"");
        }
        if (rule.Srq is { } srq)
        {
            builder.AppendLine(inv, $"{SrqField} = 0x{srq:X2}");
        }
    }

    private static Result<MockScenario, ScenarioStoreError> ParseFlatRules(
        ScenarioName name,
        string? idnDefault,
        TomlTableArray sceneTables
    )
    {
        var rules = ImmutableArray.CreateBuilder<MockRule>();
        foreach (var table in sceneTables)
        {
            var ruleResult = ParseRule(table);
            if (ruleResult is not Result<MockRule, ScenarioStoreError>.Ok ok)
            {
                return Result.Failure<MockScenario, ScenarioStoreError>(
                    ((Result<MockRule, ScenarioStoreError>.Error)ruleResult).Err
                );
            }
            rules.Add(ok.Value);
        }
        return Result.Success<MockScenario, ScenarioStoreError>(
            MockScenario.SingleScene(name, idnDefault, rules.ToImmutable())
        );
    }

    private static Result<MockScenario, ScenarioStoreError> ParseMultiScene(
        ScenarioName name,
        string? idnDefault,
        string? initialSceneRaw,
        TomlTableArray sceneTables
    )
    {
        var scenes = ImmutableArray.CreateBuilder<MockScene>();
        foreach (var table in sceneTables)
        {
            if (
                !table.TryGetValue(NameField, out var nameValue)
                || nameValue is not string sceneNameRaw
            )
            {
                return Fail("multi-scene `[[scenes]]` table is missing `name`");
            }
            if (
                SceneName.From(sceneNameRaw)
                is not Result<SceneName, SceneNameError>.Ok { Value: var sceneName }
            )
            {
                return Fail($"invalid scene name: {sceneNameRaw}");
            }

            var ruleBuilder = ImmutableArray.CreateBuilder<MockRule>();
            if (table.TryGetValue(RulesArray, out var rulesValue))
            {
                if (rulesValue is not TomlTableArray ruleTables)
                {
                    return Fail(
                        $"expected `[[scenes.rules]]` for scene `{sceneNameRaw}` to be an array of tables"
                    );
                }
                foreach (var ruleTable in ruleTables)
                {
                    var ruleResult = ParseRule(ruleTable);
                    if (ruleResult is not Result<MockRule, ScenarioStoreError>.Ok ok)
                    {
                        return Result.Failure<MockScenario, ScenarioStoreError>(
                            ((Result<MockRule, ScenarioStoreError>.Error)ruleResult).Err
                        );
                    }
                    ruleBuilder.Add(ok.Value);
                }
            }
            scenes.Add(new MockScene(sceneName, ruleBuilder.ToImmutable()));
        }

        // Resolve the initial scene: explicit > first scene > default.
        SceneName initial;
        if (initialSceneRaw is { Length: > 0 })
        {
            if (
                SceneName.From(initialSceneRaw)
                is not Result<SceneName, SceneNameError>.Ok { Value: var parsed }
            )
            {
                return Fail($"invalid initial_scene name: {initialSceneRaw}");
            }
            initial = parsed;
        }
        else if (scenes.Count > 0)
        {
            initial = scenes[0].Name;
        }
        else
        {
            initial = SceneName.DefaultScene();
            scenes.Add(MockScene.Empty(initial));
        }

        if (scenes.All(s => s.Name != initial))
        {
            return Fail($"initial_scene `{initial.Value}` does not exist in this scenario");
        }

        return Result.Success<MockScenario, ScenarioStoreError>(
            new MockScenario(name, initial, idnDefault, scenes.ToImmutable())
        );
    }

    private static Result<MockRule, ScenarioStoreError> ParseRule(TomlTable table)
    {
        if (
            !table.TryGetValue(MatchField, out var matchValue)
            || matchValue is not string match
            || string.IsNullOrEmpty(match)
        )
        {
            return FailRule("rule is missing the `match` string");
        }

        var hasRespond = table.TryGetValue(RespondField, out var respondValue);
        var hasAck = table.TryGetValue(AckField, out var ackValue);
        var hasFail = table.TryGetValue(FailField, out var failValue);

        var setCount = (hasRespond ? 1 : 0) + (hasAck ? 1 : 0) + (hasFail ? 1 : 0);
        if (setCount != 1)
        {
            return FailRule(
                $"rule for `{match}` must set exactly one of `respond`, `ack`, or `fail`"
            );
        }

        SceneName? transition = null;
        if (
            table.TryGetValue(TransitionToField, out var transValue)
            && transValue is string transRaw
            && transRaw.Length > 0
        )
        {
            if (
                SceneName.From(transRaw)
                is not Result<SceneName, SceneNameError>.Ok { Value: var t }
            )
            {
                return FailRule($"rule for `{match}`: invalid `transition_to` name '{transRaw}'");
            }
            transition = t;
        }

        byte? srq = null;
        if (table.TryGetValue(SrqField, out var srqValue))
        {
            if (srqValue is not long srqRaw || srqRaw is < 0 or > 255)
            {
                return FailRule($"rule for `{match}`: `srq` must be an integer 0..255");
            }
            srq = (byte)srqRaw;
        }

        RuleAction action;
        if (hasRespond)
        {
            if (respondValue is not string respondText)
            {
                return FailRule($"rule for `{match}`: `respond` must be a string");
            }
            action = new RuleAction.Respond(respondText, transition);
        }
        else if (hasAck)
        {
            if (ackValue is not bool ackBool || !ackBool)
            {
                return FailRule($"rule for `{match}`: `ack` must be the boolean true");
            }
            action = new RuleAction.Ack(transition);
        }
        else
        {
            if (failValue is not string failVariant)
            {
                return FailRule($"rule for `{match}`: `fail` must be a string variant tag");
            }
            string? detail = null;
            if (
                table.TryGetValue(FailDetailField, out var failDetailValue)
                && failDetailValue is string detailString
            )
            {
                detail = detailString;
            }
            action = new RuleAction.Fail(failVariant, detail, transition);
        }

        return Result.Success<MockRule, ScenarioStoreError>(new MockRule(match, action, srq));
    }

    private static Result<MockScenario, ScenarioStoreError> Fail(string reason) =>
        Result.Failure<MockScenario, ScenarioStoreError>(new ScenarioStoreParseFailure(reason));

    private static Result<MockQuirks?, ScenarioStoreError> FailQuirks(string reason) =>
        Result.Failure<MockQuirks?, ScenarioStoreError>(new ScenarioStoreParseFailure(reason));

    private static Result<MockRule, ScenarioStoreError> FailRule(string reason) =>
        Result.Failure<MockRule, ScenarioStoreError>(new ScenarioStoreParseFailure(reason));

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
