# 0013. Plugin System

- Status: Accepted
- Date: 2026-05-28

## Context

Some instrument families ship their own transports, lock managers,
or vendor protocols that don't fit the existing
`IIviBackend` matrix (HiSlip / VXI-11 / Socket / Local). Asking
every operator to fork ivi-cli to add a backend for their lab's
specialised instrument is the wrong default — most third parties
won't ever upstream their code, and pinning a downstream fork
means missing core security patches.

This ADR defines a stable in-process plugin contract so vendors
ship a separate DLL that ivi-cli loads at startup, registers as
just another `IIviBackend`, and routes to via the existing
`IBackendFactory` decorator stack.

## Decision

### 1. Contract assembly: `IviCli.Plugin`

A new top-level assembly publishes the ABI plugin authors compile
against. v1 surface:

```csharp
public interface IIviPlugin
{
    string Name { get; }
    string Version { get; }
    int TargetApiVersion { get; }   // must equal HostApiVersion.Current
    void Register(IPluginServices services);
}

public interface IPluginServices
{
    void AddBackend<TBackend>(VisaResourceMatcher matcher)
        where TBackend : class, IIviBackend;
}

public delegate bool VisaResourceMatcher(VisaResource resource);

public static class HostApiVersion { public const int Current = 1; }
```

The Plugin assembly references `IviCli.Application` (for
`IIviBackend`) and `IviCli.Domain` (for `VisaResource`). Plugin
authors therefore reference exactly two ivi-cli assemblies. The
narrow `IPluginServices` surface keeps the host's internals out
of plugin sight.

### 2. Discovery layout

```
${IVICLI_DATA_DIR}/plugins/
  ├── acme-instruments/
  │   ├── plugin.toml
  │   └── IviCli.Plugin.Acme.dll  (+ transitive deps)
  └── vendor-x/
      ├── plugin.toml
      └── ...
```

Each plugin lives in its own subdirectory. `PluginLoader.LoadAll`
iterates the subdirectories and processes each independently — a
malformed plugin logs at Warning and the loader continues.

`IVICLI_PLUGINS_DIR` overrides the default location; the resolver
mirrors `ResolveAuditDirectory` / `ResolveAuthDirectory`
(Windows: `LocalApplicationData/ivi-cli/plugins`,
Linux/macOS: `$XDG_CONFIG_HOME/ivi-cli/plugins`).

### 3. Manifest (`plugin.toml`)

```toml
[plugin]
name = "acme-instruments"
version = "1.0.0"
target_api_version = 1
entry_point = "Acme.Plugin.AcmePlugin"
assembly = "IviCli.Plugin.Acme.dll"
```

Five required string / int fields. Validated by
`PluginManifest.From` which returns specific failure variants
(`PluginManifestFieldBlank`, `PluginManifestInvalidApiVersion`).
Manifest parse errors and field violations surface as the
`PluginManifestSyntaxError` family — the loader logs and skips
the plugin.

### 4. Loading mechanism

`PluginLoader` (Infrastructure) drives:

1. Read `plugin.toml`, parse via Tomlyn.
2. Reject plugins whose `target_api_version != HostApiVersion.Current`
   (`PluginApiVersionMismatch`). v1 host API = 1.
3. Consult the operator's allowlist (`PluginsConfig.IsAllowed`);
   reject as `PluginNotAllowed` when the manifest's name isn't
   listed (empty list = allow every discovered name).
4. Verify the DLL exists at `<subdir>/<assembly>`.
5. Create one `AssemblyLoadContext(name, isCollectible: false)`
   per plugin so colliding transitive deps in two plugins don't
   step on each other. v1 contexts are not collectible — plugin
   unload is a v2 follow-up.
6. Resolve `manifest.EntryPoint` via `assembly.GetType` and verify
   it implements `IIviPlugin`. Both rejection paths return
   specific error variants.
7. `Activator.CreateInstance(entryType)` instantiates the plugin.
8. The composition root calls `plugin.Register(pluginServices)`.
   Plugin `AddBackend<T>()` calls accumulate as
   `IServiceCollection.AddSingleton<T>()` registrations plus a
   `PluginBackendRegistration(typeof(T), matcher)` entry on a
   list the host consumes next.

### 5. Routing: `PluginBackendFactory`

A new `IBackendFactory` decorator (Infrastructure) consults
plugin registrations before delegating to the inner factory.
Matchers run in registration order; first match wins. Decorator
stack order in `Program.cs` becomes:

```
Capture > Pool > Plugin > Instrumenting > Default
```

The plugin layer sits inside the pool (plugins get the same idle/
LRU eviction and concurrency cap as built-in backends) but
outside Instrumenting (the Activity / Meter telemetry still
captures plugin op duration).

### 6. Configuration

```toml
[plugins]
enabled = false                     # default OFF
allowed = ["acme-instruments"]      # optional allowlist
```

**Default-off** because plugin DLLs run in-process with the same
permissions as the CLI. Operators must explicitly opt in to mark
"I trust the code in this directory."

`PluginsConfig.IsAllowed`: an empty list permits every discovered
name; a populated list restricts to listed names only. Mistyped
plugin names in the allowlist surface as `PluginNotAllowed`
warnings rather than silent skips.

### 7. Security stance

- **Trust model**: plugins are in-process, full-trust C#. Same
  privileges as the CLI binary. Operators choosing to install a
  plugin DLL accept that risk in the same way they accept
  installing any other native software.
- **Allowlist gating**: protects against accidental
  loading of a binary the operator forgot they dropped into
  `${IVICLI_DATA_DIR}/plugins/`. Not a substitute for vetting
  the binary itself.
- **No signature validation v1**: a v2 candidate is an Ed25519
  signature requirement + pubkey fingerprint allowlist.
- **No network sandbox / capability restrictions**: also v2.

### 8. Out of scope (v1)

- **Gateway-server pluggability** — only `IIviBackend` is
  pluggable in v1. New gateway protocols stay first-party until
  the contract surface for `IGatewayServer` is fleshed out.
- **Hot reload / unload** — `AssemblyLoadContext` is created
  with `isCollectible: false`. Reloading plugins requires
  restarting the CLI.
- **Ed25519 signature validation + pubkey fingerprint allowlist.**
- **Native-code dependencies** — plugins are pure managed.
  Wrapping vendor C SDKs into plugin assemblies is the plugin
  author's problem.
- **Plugin-specific config injection** — plugins read their own
  config via whatever shape they choose; v2 may add a
  `[plugins.<name>]` subtable surfaced through `IPluginServices`.
- **Cross-AssemblyLoadContext type isolation** — both the host
  and the plugin reference `IviCli.Plugin` from the same shared
  assembly load context (the default ALC). Plugins that reference
  different versions of the contract assembly will fail at type-
  assignability check time with `PluginEntryPointNotIIviPlugin`.

## Consequences

- **Vendor extensibility**: third parties ship backends without
  forking ivi-cli; operators install a directory, flip
  `[plugins].enabled`, restart.
- **Stable plugin ABI**: every breaking change to `IIviBackend`
  forces a `HostApiVersion.Current` bump, refusing plugins built
  against the old surface. Additive `IIviBackend` extensions in
  Batch P (TriggerAsync, ServiceRequestStream) are why
  `TargetApiVersion` starts at 1 with this commit — the
  follow-up Batch T schedule begins API v2.
- **Sample plugin in repo**: `tests/IviCli.Plugin.Sample`
  doubles as a reference implementation; vendors can clone it
  as a starting point.
- **No security regression by default**: default-off keeps the
  `ivicli` binary's blast radius unchanged for the existing
  audience.

## Verification

- `dotnet test --filter "Category!=Integration"` covers the
  manifest validation, every `PluginLoader.LoadOne` failure
  branch via `MockFileSystem`, and an end-to-end test that
  stages the sample plugin into a temp directory, drives
  `PluginLoader.LoadAll`, calls `Register`, builds a real
  `PluginBackendFactory`, and asserts the matching device routes
  to the plugin's backend.
- Manual: build any IIviPlugin-implementing class library, drop
  it under `${IVICLI_DATA_DIR}/plugins/<name>/`, write a
  `plugin.toml`, set `[plugins].enabled = true`, run
  `ivicli api start`, observe `loaded plugin <name>` in the log.
