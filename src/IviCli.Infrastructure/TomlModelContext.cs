using Tomlyn.Model;
using Tomlyn.Serialization;

namespace IviCli.Infrastructure;

/// <summary>
/// Source-generated Tomlyn context for the document model (issue #15).
/// Every TOML surface in this assembly — config, scenarios, API tokens,
/// plugin manifests — maps <see cref="TomlTable"/> by hand, so the one
/// generated type info keeps all of them off the reflection path.
/// </summary>
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class TomlModelContext : TomlSerializerContext;
