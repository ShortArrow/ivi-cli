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
/// </summary>
public static class TomlScenarioParser
{
    private const string IdnField = "idn";
    private const string ScenesArray = "scenes";
    private const string MatchField = "match";
    private const string RespondField = "respond";
    private const string AckField = "ack";
    private const string FailField = "fail";
    private const string FailDetailField = "fail_detail";

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
            model = Toml.ToModel(toml);
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

        var scenes = ImmutableArray.CreateBuilder<MockScene>();
        if (model.TryGetValue(ScenesArray, out var scenesValue))
        {
            if (scenesValue is not TomlTableArray sceneTables)
            {
                return Fail("expected `[[scenes]]` to be an array of tables");
            }

            foreach (var table in sceneTables)
            {
                var sceneResult = ParseScene(table);
                if (sceneResult is not Result<MockScene, ScenarioStoreError>.Ok sceneOk)
                {
                    return Result.Failure<MockScenario, ScenarioStoreError>(
                        ((Result<MockScene, ScenarioStoreError>.Error)sceneResult).Err
                    );
                }
                scenes.Add(sceneOk.Value);
            }
        }

        return Result.Success<MockScenario, ScenarioStoreError>(
            new MockScenario(name, idnDefault, scenes.ToImmutable())
        );
    }

    /// <summary>Serializes a scenario back to TOML.</summary>
    public static string Serialize(MockScenario scenario)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var builder = new StringBuilder();

        if (scenario.IdnDefault is { } idn)
        {
            builder.AppendLine(inv, $"idn = \"{Escape(idn)}\"");
            builder.AppendLine();
        }

        foreach (var scene in scenario.Scenes)
        {
            builder.AppendLine("[[scenes]]");
            builder.AppendLine(inv, $"match = \"{Escape(scene.Match)}\"");
            switch (scene.Action)
            {
                case SceneAction.Respond r:
                    builder.AppendLine(inv, $"respond = \"{Escape(r.Text)}\"");
                    break;
                case SceneAction.Ack:
                    builder.AppendLine("ack = true");
                    break;
                case SceneAction.Fail f:
                    builder.AppendLine(inv, $"fail = \"{Escape(f.Variant)}\"");
                    if (f.Detail is { } detail)
                    {
                        builder.AppendLine(inv, $"fail_detail = \"{Escape(detail)}\"");
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"unknown SceneAction variant: {scene.Action.GetType().Name}"
                    );
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static Result<MockScene, ScenarioStoreError> ParseScene(TomlTable table)
    {
        if (
            !table.TryGetValue(MatchField, out var matchValue)
            || matchValue is not string match
            || string.IsNullOrEmpty(match)
        )
        {
            return FailScene("scene is missing the `match` string");
        }

        var hasRespond = table.TryGetValue(RespondField, out var respondValue);
        var hasAck = table.TryGetValue(AckField, out var ackValue);
        var hasFail = table.TryGetValue(FailField, out var failValue);

        var setCount = (hasRespond ? 1 : 0) + (hasAck ? 1 : 0) + (hasFail ? 1 : 0);
        if (setCount != 1)
        {
            return FailScene(
                $"scene for `{match}` must set exactly one of `respond`, `ack`, or `fail`"
            );
        }

        SceneAction action;
        if (hasRespond)
        {
            if (respondValue is not string respondText)
            {
                return FailScene($"scene for `{match}`: `respond` must be a string");
            }
            action = new SceneAction.Respond(respondText);
        }
        else if (hasAck)
        {
            if (ackValue is not bool ackBool || !ackBool)
            {
                return FailScene($"scene for `{match}`: `ack` must be the boolean true");
            }
            action = new SceneAction.Ack();
        }
        else
        {
            if (failValue is not string failVariant)
            {
                return FailScene($"scene for `{match}`: `fail` must be a string variant tag");
            }
            string? detail = null;
            if (
                table.TryGetValue(FailDetailField, out var failDetailValue)
                && failDetailValue is string detailString
            )
            {
                detail = detailString;
            }
            action = new SceneAction.Fail(failVariant, detail);
        }

        return Result.Success<MockScene, ScenarioStoreError>(new MockScene(match, action));
    }

    private static Result<MockScenario, ScenarioStoreError> Fail(string reason) =>
        Result.Failure<MockScenario, ScenarioStoreError>(new ScenarioStoreParseFailure(reason));

    private static Result<MockScene, ScenarioStoreError> FailScene(string reason) =>
        Result.Failure<MockScene, ScenarioStoreError>(new ScenarioStoreParseFailure(reason));

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
