# Runtime and modules

Lua plugins run in MoonSharp's hard sandbox. Arbitrary .NET objects, operating-system processes and unrestricted file access are not exposed.

## Callback execution

Each plugin processes events, timers and settings changes on one queue. A Lua invocation has a **100,000-instruction** budget; exceeding it or yielding raises an error. The event queue holds **512** operations. Keep callbacks short.

Command and HTTP calls block the plugin queue until they finish. Catch expected failures:

```lua
local ok, result = pcall(function()
    return vars.serverName()
end)

if not ok then
    log.warning(tostring(result))
end
```

Disabling a plugin clears timers and handlers. Re-enabling reloads the Lua script. Saved settings are restored before `plugin_enable`. Persistent storage survives a reload and is isolated by server and absolute plugin path; moving a plugin changes its storage identity.

## require

```lua
local api = require("lightrcon")
local players = require("players")
local messages = require("helpers.messages")
```

`lightrcon` returns the globals table. A built-in namespace name returns that namespace. Other names resolve to `.lua` files beneath the plugin's directory: `helpers.messages` loads `helpers/messages.lua`.

Local modules are cached. A module that returns nil is cached as `true`. Circular imports, missing files and paths outside the plugin directory raise errors. Modules must stay with the plugin when it is moved or copied.

## Layer

A Layer client's plugin controls operate on the upstream runtime. View permission allows inspection, Settings permission allows configuration changes, and LayerAdmin permission allows activation changes. A plugin runs with its host session's command access; client account permissions govern remote management.
