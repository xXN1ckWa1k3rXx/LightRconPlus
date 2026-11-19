# Plugins in Pre-alpha 1.0

Connect to a server, open **Plugin**, click **Add plugin…**, and select a local `.cs`, `.dll` or `.lua` file. Adding a plugin loads its metadata and settings without enabling it. Select **Enable** to start it, or **Disable** to stop it. Disabled plugins remain in the list. You can also drop files on the list. Imports ask whether to keep the original location or copy the package into `plugins/BF3`, `plugins/BF4` or `plugins/BFH`; change the default in Settings. Refresh list discovers new library entries for the selected game.

**Details** shows the plugin's name, version, author, website, description, source path and errors. **Plugin Settings** displays the settings defined by the plugin, grouped by their original labels. Boolean, number, enum, multiline, string-array and read-only fields are supported. Edited values are applied and saved per server when the control loses focus. Multiline inputs provide an expanded editor. Reload beside Enable/Disable requests fresh variables, including settings exposed conditionally by the plugin.

The desktop and headless hosts reload installed plugins and restore enabled states after reconnect. Source paths must remain available. Plugin settings are stored in the server profile. C# plugins run with the current user's OS permissions.

## Procon v1

The compatibility implementation uses the complete API assembly, compiler, event dispatcher, scheduler and plugin services from [AdKats Procon v1.5.4.2](https://github.com/AdKats/Procon-1/tree/v1.5.4.2). It does not replace those types with a small collection of stubs.

All Procon plugins for one server share one worker, so plugin-to-plugin calls, registered commands and scheduled Procon tasks can interact. The worker connects through a loopback Frostbite bridge; commands use the selected LightRcon+ session, including Layer permissions. Before compiling plugins, the adapter maps Procon's connection identity to the selected instance endpoint, so `OnPluginLoaded` receives the configured server host and port. Worker readiness waits for initial compilation to finish, avoiding concurrent compiler calls during startup.

Source plugins are compiled before loading. Precompiled Procon DLLs are loaded through the upstream cache. Filenames must match class names. Adjacent DLL dependencies, managed assemblies with a `.cs` extension, matching partial sources, included files and common `Includes`/`Shared` directories are staged. Binary `.cs` dependencies remain available to `Assembly.LoadFrom` and are excluded from source compilation. `#include` paths resolve from the game plugin directory, matching Procon, including parent-directory includes. Keep the original plugin package folder layout.

BF4DB 2.0.16 requires both `BF4DB.cs` and `BF4DB_API.cs` in the original package directory. The second file is a managed assembly, despite its extension. Loading the unmodified package against the local simulator exposes the plugin metadata and 11 settings without enabling it. Enabling BF4DB can make synchronous HTTP requests through its helper; local loading tests do not validate API keys or the external service.

The compatibility runtime first downloads the upstream release and verifies its pinned SHA-256. For offline installations, copy the original `Procon-v1.5.4.2.zip` into the profile store root. The worker requires Windows .NET Framework 4.x; 4.8 is recommended. The per-server worker directory is persistent at `servers/<id>/plugins/procon`, so plugin-created files survive reconnects. Adding another Procon plugin restarts this shared worker and restores settings and enabled states.

The yellow compatibility banner is translated into English, Chinese and German. It appears inside the selected plugin's content area for Procon plugins, including disabled plugins and load failures. The original Procon WinForms control menu is represented by LightRcon+'s Details and Plugin Settings pages. HTML descriptions are shown as readable text. The current plugin's blue Enable or Disable action stays beside its name above both pages.

Opening a failed plugin's details or settings returns the recorded error without rebuilding the shared runtime. After correcting its files, use Reload, Refresh list or Enable on the host to retry loading. Compilation output is forwarded to the server console.

Load failures include a recent log excerpt. The adapter reads active Procon logs with file sharing and completes failed worker requests even if diagnostic processing fails, so an error response cannot turn into a 50-second request timeout.

Disconnecting or closing during restoration cancels pending startup and stops restoring additional plugins.

## Plugins through Layer

A Layer client displays only the plugins installed on the upstream host. Plugin code runs once on that host; connecting clients do not load local plugin files or create additional runtimes. Add source files and DLLs on the host. Installed-list and activation changes are pushed to connected clients; Details and Reload retrieve current metadata and settings from the host.

View permission allows reading the list, details and settings. Settings permission allows saving plugin settings on the host; LayerAdmin permission allows enabling and disabling installed plugins. Validation, read-only restrictions and persistence run on the host. Remote requests cannot register or open arbitrary host paths. Disconnecting a client leaves the host's plugins running, and reconnecting fetches the current upstream state.

Integration coverage includes unmodified upstream **CSpambot** and **CInGameAdmin**, source and binary plugins, event callbacks, grouped settings, read-only values, string-array encoding, plugin-to-plugin calls and Lua interop. This establishes tested behavior; production servers and arbitrary third-party plugins still require validation.

## Native C# API v1

Select a standalone source file, or build a .NET 10 class library referencing `LightRcon.Core.dll`. Declare the API version at assembly scope. Unsupported versions fail activation. Export one concrete plugin class.

Published packages include the reference assembly in `api/csharp/LightRcon.Core.dll` and a source example in `examples/plugins/JoinLogger.cs`.

```csharp
using LightRcon.Core.Plugins;
[assembly: LightRconPlus_only(1)]

public sealed class JoinLogger : ILightRconPlugin
{
    private PluginContext context = null!;
    public void Enable(PluginContext context) => this.context = context;
    public void OnEvent(string[] words)
    {
        if (words.Length >= 2 && words[0] == "player.onJoin")
            context.Log(words[1] + " joined");
    }
    public void Dispose() { }
}
```

`CommandAsync(params string[] words)` returns the response words. Events are serialized on the plugin's queue. Implement the optional `IPluginConfiguration` interface to supply `Details`, `GetSettings()` and `SetSetting(name, value)`; these populate the same pages as Procon and Lua plugins.

## Lua API v1

The API package contains [the full Lua reference](LUA-API.md), the machine-readable [API manifest](../api/lightrcon-api.json) and [example plugins](../examples/plugins). Modules are available globally or through `require("lightrcon")` and `require("players")`. Local modules can be loaded with `require("helpers.messages")`, resolving to `helpers/messages.lua` under the main plugin directory.

```lua
plugin = {
    api_version = 1,
    name = "Respawn announcement",
    version = "1.0",
    author = "Your name",
    description = "Announce player respawns."
}

settings.define("Messages|Seconds", "int", 5)

event.player_respawn:set(function(self)
    admin.yell("all", self.get_name() .. " is respawn", settings.get("Messages|Seconds"))
end)
```

Supported namespaces cover administration, player objects, server information, maps, bans, reserved slots, spectators, server variables, plugin metadata/settings, lifecycle and game events, timers, persistent storage, logging, JSON, HTTP, raw RCON and Procon services. No server commands need to be built as quoted command strings: pass protocol words as separate arguments.

Lua execution uses MoonSharp hard-sandbox modules. Each callback has a 100,000-instruction budget. Plugin callbacks and timers run serially. Errors appear in the plugin Details page and server console. Persistent Lua data is isolated by server and plugin path.
