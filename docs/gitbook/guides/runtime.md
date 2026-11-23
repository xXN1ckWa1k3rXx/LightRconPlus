# Runtime and modules

Lua plugins run in MoonSharp's hard sandbox. Arbitrary .NET objects, operating-system processes and unrestricted file access are not exposed.

## Callback execution

Each plugin processes events, timers and settings changes on one queue. A Lua invocation has a **100,000-instruction** budget; exceeding it or yielding raises an error. The event queue holds **512** operations. Keep callbacks short.

`pcall` and `xpcall` are enabled for handling expected errors. The host converts failed API calls into catchable Lua errors. The instruction budget also stops an infinite loop inside `pcall`; it never resumes that callback automatically. A later event runs in a fresh coroutine. A failed timer is removed without stopping unrelated timers.

Uncaught load, syntax, event, setting and timer errors appear in Plugin console with a script filename, source location and `stack traceback` caller frames. Module names are relative to the plugin directory. Absolute Windows, UNC and Unix paths in Lua print/log/exception output are redacted before storage or Layer forwarding. Useful filenames and script line numbers remain visible. Interactive breakpoints and single stepping are not provided.

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

Successful Layer sign-ins and **Refresh list** check the host's game-specific plugin library for new or changed files. The host also scans when its RCON connection is established. There is no periodic file scan. After a library update, saved settings and enabled states are restored; unchanged files do not trigger a reload on sign-in.
