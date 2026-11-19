# Getting started

Create a UTF-8 file named `RespawnAnnouncement.lua`:

```lua
plugin = {
    api_version = 1,
    name = "Respawn announcement",
    version = "1.0",
    author = "Server admin",
    description = "Announces player respawns."
}

event.player_respawn:set(function(self)
    admin.yell("all", self.get_name() .. " has respawned", 5)
end)
```

Connect to the game server, open **Plugin**, and select **Add plugin…**. Choose the Lua file, then choose whether to keep its location or copy it into the plugin library. **Enable** starts the plugin; **Disable** stops it.

On a Layer client, the plugin list and settings belong to the upstream host. Import files on that host. Connecting another client does not start another copy of a plugin.

The library uses `plugins/BF3/`, `plugins/BF4/` and `plugins/BFH/` beside the executable. Copied packages live in `GAME/PluginName/`. Keep helper modules and dependencies inside the package. **Refresh list** discovers entries for the connected game.

Settings save when their controls lose focus. **Reload** reads the plugin's current fields and values. Check **Details** and **Console** for loading or callback errors.

## Conventions

* Lua arrays start at **1**. Protocol map indexes and ban indexes start at **0**.
* Namespace functions and player methods use a dot: `players.get("Player001")`, `p.get_name()`. Event registration supports `event.player_respawn:set(callback)`.
* Square brackets in a reference signature mark optional arguments; omit the brackets when writing Lua.
* A server command returns response words, including `"OK"`. A rejected command raises an error; use `pcall` where failure is expected.
* Snapshot getters return cached session data. They do not synchronously refresh players, maps or bans.
* Game support varies by command. An exposed wrapper does not imply that every game accepts it.

See [Runtime and modules](../guides/runtime.md) for sandbox limits and module loading.
