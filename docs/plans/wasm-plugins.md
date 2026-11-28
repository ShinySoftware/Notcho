# Plan: WASM plugins

Let people add their own tabs to Notcho without us shipping them, and without giving up Native AOT. Plugins are WebAssembly modules run in a sandbox inside the app. A plugin can't touch the file system, the network or the UI directly: it can only call the host functions we give it, and it describes its tab as data that Notcho renders with its own controls.

This plan covers:

1. **The runtime and the contract.** How a `.wasm` file gets loaded, validated and called, and what it must export.
2. **Plugin tabs.** How a plugin's output becomes a tab in the strip, a peek on the closed notch, and a page in Settings.
3. **Authoring and distribution.** What a plugin author writes, how a plugin is packaged, installed and trusted.

Out of scope: plugins that ship native code or MAUI views, plugins that replace or modify built-in tabs, a plugin store or catalogue.

## What we know today

- **Notcho isn't AOT yet.** No head sets `PublishAot`, and the macOS `Entitlements.plist` declares `com.apple.security.cs.allow-jit` for CoreCLR's JIT. The plugin system must work under AOT so that it doesn't block the move, but doesn't depend on it.
- **Three heads: macOS (AppKit), Windows, Linux (GTK4).** A plugin must run unchanged on all three, so the WASM runtime needs native libraries for osx-arm64, osx-x64, win-x64, win-arm64, linux-x64 and linux-arm64.
- **Tabs are a closed enum.** `NotchTab` in `Views/PanelView.cs` lists every tab, `NotchTabInfo.All` gives the strip order, and `NotchController` switches on the enum to open, refresh and rebuild. Hidden tabs are stored by name (`AppSettings.HiddenTabs`), as is `LastTab`. Plugin tabs need an identity alongside the enum that doesn't break either setting.
- **Distribution is Developer ID + notarization through GitHub releases, not the Mac App Store.** We can load code the user installs. Since WASM is data to the runtime rather than a native library, we don't need `disable-library-validation`.
- **The runtime JITs WASM to machine code.** Wasmtime (which Extism is built on) compiles with Cranelift, which needs `allow-jit` under the hardened runtime. We already declare it for CoreCLR. Once the app goes AOT, the entitlement stays because plugins need it.
- **Neither WASM host SDK has been tested under AOT.** The Extism and Wasmtime .NET SDKs are P/Invoke wrappers, so they should work, but the callback wrapping is where reflection could sneak in. Phase 0 checks.
- **Component Model / WIT hosting in .NET isn't there yet**, as far as we know: wasmtime-dotnet runs core modules only. We use a hand-written contract on core modules and can move to WIT later without changing what plugins do.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Runtime | Extism (on Wasmtime), unless Phase 0 rules it out | Handles memory and passing strings for us. Has an allowed-hosts list, memory limits and timeouts built into its manifest. Has plugin SDKs (PDKs) for Rust, Go, JS, C#, Zig and others. Fallback: raw Wasmtime .NET with the export-signature checks in Phase 1. |
| UI | Plugins return a declarative UI tree as JSON; Notcho renders it with `Ui.*` | MAUI views can't cross the boundary, and this keeps plugin tabs looking like ours on every head. |
| Serialization | JSON with `System.Text.Json` source-generated contexts | AOT-safe, readable when debugging, every PDK has it. MessagePack later only if profiling says so. |
| Threading | One plugin instance per plugin, calls serialized on a background thread, results marshalled to the UI thread | WASM instances aren't thread-safe; a slow plugin must never block the notch. |
| Identity | Reverse-DNS id in the manifest (`com.example.stocks`); stored in settings as `plugin:<id>` | Can't clash with built-in tab names in `HiddenTabs` and `LastTab`. |

## The contract

### What a plugin exports

Every export takes JSON in and returns JSON out (Extism's input/output), except `api_version`.

| Export | Required | Input → output | Called when |
|---|---|---|---|
| `api_version` | yes | → `int` | on load; must equal the host's supported version |
| `info` | yes | → `PluginInfo` (name, symbol, refresh interval, settings schema) | on load |
| `render` | yes | `RenderRequest` (width, theme, settings, last state) → `UiNode` tree | the tab opens, a refresh fires, or the plugin asks for one |
| `on_action` | yes | `ActionEvent` (action id, value) → `ActionResult` (re-render?, open URL?, toast?) | the user clicks a button or chip, or edits a field the plugin rendered |
| `peek` | no | → `PeekContent` (symbol, short text, tint) or null | on the refresh interval, while the notch is closed; this is how live scores show on the notch today |
| `on_settings_changed` | no | `SettingsBag` → nothing | the user changes a plugin setting |

### What the host provides (Extism host functions, namespace `notcho`)

| Function | Notes |
|---|---|
| `log(level, message)` | to the app log, prefixed with the plugin id |
| `kv_get(key)` / `kv_set(key, value)` | storage scoped to the plugin id, size-capped |
| `now()` | UTC time from the app's `TimeProvider`, so tests can fake it |
| `request_render()` | asks the host to call `render` again soon |
| HTTP | Extism's built-in `http_request`, limited to the manifest's `allowedHosts` |

Anything not in this list can't be done. A plugin that imports something we don't provide fails at load time.

### The UI tree

A small, closed set of node types, each mapped to the `Ui` helpers the built-in tabs already use:

`stack` (vertical/horizontal, spacing) · `card` · `label` (text, size, bold, secondary) · `symbol` (SF Symbol name, mapped on Windows/Linux like ours) · `chip` (text, selected, action id) · `button` (text, symbol, action id) · `list` (rows, scrolls, max height) · `image` (URL from an allowed host, or a data URI) · `progress` · `entry` (text input, action id on commit).

Unknown node types render as nothing and log once, so newer plugins degrade gracefully on older hosts. Width is fixed at the panel width the host passes in; plugins can't make the panel wider.

### Versioning rules

- `api_version` starts at 1 and is checked on load. A mismatch means the plugin shows as "needs an update" in Settings and doesn't run.
- Never change an existing export's input or output in a breaking way. Add fields; don't remove or rename them.
- New capabilities arrive as new optional exports or new node types, not a version bump. The version bumps only when something must break.

## Host architecture

New code in `Notcho.Core/Plugins/`:

- **`PluginManifest`**: `manifest.json` read with a source-generated context. Fields: id, name, version, author, `apiVersion`, symbol, `allowedHosts`, memory limit, and a description that Settings shows.
- **`PluginLoader`**: finds installed plugins, reads each manifest, builds the Extism manifest from it (wasm path, allowed hosts, memory pages, timeout), creates the plugin with our host functions, and validates it. Validation means every required export exists (`FunctionExists`) and `api_version` matches. A plugin that fails is kept in the list with its error so Settings can say why, but never runs.
- **`WasmNotchPlugin : INotchPlugin`**: wraps one Extism plugin behind a C# interface. Owns the call queue (one call at a time, on a background thread), the per-call timeout, and JSON in and out. After three failures in a row (traps or timeouts) it disables itself until the next launch and says so in its tab.
- **`PluginService`** (`[Singleton]`): the installed plugins, `Changed` event, install/remove/enable, refresh timers for `peek` and `render` (only while the tab shows or the notch is closed with a peek, in line with the app's low-power rule).
- **`UiNodeRenderer`**: turns a `UiNode` tree into MAUI views with `Ui.Card`, `Ui.Label`, `Ui.Chip`, `Ui.Symbol` and the rest. Clicks call back into `PluginService` with the action id.

```csharp
public interface INotchPlugin
{
    PluginInfo Info { get; }
    Task<UiNode> RenderAsync(RenderRequest request, CancellationToken ct);
    Task<ActionResult> OnActionAsync(ActionEvent action, CancellationToken ct);
    Task<PeekContent?> PeekAsync(CancellationToken ct);
}
```

### Wiring into tabs

- `NotchTabInfo` gains an optional plugin id. The `NotchTab` enum gets one `Plugin` member, and a plugin tab is `(NotchTab.Plugin, "com.example.stocks")`. `ShownTabs` appends enabled plugins after the built-in tabs, filtered by `HiddenTabs` using the `plugin:<id>` name.
- `NotchController.Open` calls `RenderAsync` for plugin tabs, showing a loading card until it returns, and rebuilds on `request_render` the same way built-in services raise `Changed`.
- The peek takes the plugin's `PeekContent` like it takes live scores now. Only one plugin can own the peek at a time, chosen in Settings.

### Settings

A **Plugins** category in Settings:

- the installed plugins with name, version, author, enabled switch, and status (running, needs update, failed + reason);
- what each plugin can reach (its `allowedHosts`), shown before it's enabled and again whenever an update changes the list;
- each plugin's own settings, rendered from the schema in `info` (text, toggle, choice);
- Install (file picker, or drop a `.notchplugin` on the notch's file drop) and Remove.

Backup/restore includes which plugins are enabled and their settings, not the plugin files.

## Authoring and packaging

- **Package:** `<id>-<version>.notchplugin`, a zip holding `manifest.json`, `plugin.wasm` and an optional `README.md`. Installed under the app's data folder in `Plugins/<id>/` (Application Support on macOS, `%APPDATA%` on Windows, `~/.local/share` on Linux).
- **Notcho.Plugin.Sdk (C#):** a NuGet package on top of the Extism .NET PDK, with the contract records, the `UiNode` builders and a base class, so a plugin is "override `Render` and `OnAction`". Built for WASI with `dotnet publish`.
- **Rust crate**, same shape on the Extism Rust PDK. Rust plugins come out much smaller than .NET WASI ones, so the samples lead with Rust.
- **JSON schemas** for every contract type, generated from the C# records, so plugins in other languages have something to check against.
- **`notcho-plugin` CLI (later):** `validate` (load the wasm, check exports and manifest the same way the app does) and `pack`.
- **Samples:** a stock ticker (HTTP + peek), a counter (actions + kv storage), and a "hello" plugin used by the tests.

## Security

- **The sandbox is the boundary.** No WASI file system or environment access is granted. Network goes only to the manifest's `allowedHosts`. Memory is capped per plugin (default 64 MB) and each call has a timeout (default 2 s for `render`, 500 ms for `peek`).
- **The user approves the hosts.** Install and every update that widens `allowedHosts` asks first.
- **No signing at first.** Plugins come from wherever the user got them, like browser extensions installed by hand. A signed-publisher scheme is an open question for later.
- **Plugin output is untrusted.** URLs from `on_action` open only with `http`/`https`. Image URLs must be on an allowed host. Text is rendered as text, never markup.
- **Entitlements:** no change. `allow-jit` is already there; no `disable-library-validation`.

## Phases

### Phase 0: spike (1-2 days)

1. Add Extism's .NET SDK and runtime packages to a throwaway console app with `PublishAot=true`, for osx-arm64, win-x64 and linux-x64.
2. Load a Rust "hello" plugin, call an export with JSON, register one host function, and hit an allowed and a blocked host.
3. Do the same in the signed, notarized, hardened macOS build.
4. Measure: package size added per RID, plugin load time, call overhead for a 10 KB JSON round trip.

**Exit criteria:** zero AOT/trim warnings from the SDK (or ones we can suppress safely), works under the hardened runtime, call overhead under 1 ms. Otherwise repeat with Wasmtime .NET and the hand-written ABI.

### Phase 1: host and contract

`PluginManifest`, `PluginLoader`, `WasmNotchPlugin`, `PluginService`, the contract records with source-generated JSON, the host functions, and the Rust "hello" plugin as a test fixture.

**Exit criteria:** unit tests load the fixture, reject a plugin with a missing export or a wrong `api_version`, enforce a timeout on a looping plugin and the memory cap on a greedy one, and disable a plugin after repeated failures.

### Phase 2: plugin tabs

`UiNodeRenderer`, the `NotchTab.Plugin` wiring in `NotchController` and `ShownTabs`, actions, `request_render`, and the peek.

**Exit criteria:** the counter and stock samples work as tabs on all three heads, look like the built-in tabs, and a plugin stuck in `render` never freezes the notch.

### Phase 3: Settings and install

The Plugins category, install from file and file drop, remove, host approval, plugin settings from the schema, backup/restore.

**Exit criteria:** a user with no developer tools can install, configure, disable and remove a plugin.

### Phase 4: SDKs and docs

The C# and Rust SDKs, JSON schemas, samples, a page on the docs site, and the `validate` CLI.

**Exit criteria:** someone outside the project builds a working plugin from the docs alone.

## Risks and open questions

- **.NET WASI plugin size.** A C# plugin compiled to WASI can be several MB and slower to start than Rust. If it's bad, C# stays supported but the docs recommend Rust or JS for small plugins.
- **Extism project health.** If it stalls, the fallback is raw Wasmtime with our own ABI. The `INotchPlugin` interface keeps that change inside `Plugins/`.
- **The UI tree will be asked to grow.** Charts, sliders and custom drawing are obvious next asks. Each new node type is a host change. The bar for adding one: two real plugins need it.
- **Symbols on Windows and Linux.** Plugins name SF Symbols; we only map the ones we use. Unknown ones need a fallback glyph, or plugins pick from a published list.
- **Should plugins get the now-playing info, calendar or system stats?** Useful, but each one is a new host function and a new permission to explain. Not in v1.
- **Signing and a catalogue.** Worth it only if third-party plugins actually appear.
