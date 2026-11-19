# Examples

## Configurable respawn announcement

```lua
plugin = { api_version = 1, name = "Respawn announcement", version = "1.0" }
settings.define("Announcement|Enabled", "bool", true)
settings.define("Announcement|Seconds", "int", 5)

event.player_respawn:set(function(self)
    if settings.get("Announcement|Enabled") then
        admin.yell("all", self.get_name() .. " has respawned",
            settings.get("Announcement|Seconds"))
    end
end)
```

Requires a connected server supporting `admin.yell`. Enable the plugin, trigger a player spawn and check the announcement. Changing a setting in the plugin page takes effect after the control loses focus.

## Persistent enable count

```lua
plugin = { api_version = 1, name = "Enable counter", version = "1.0" }

event.plugin_enable:set(function()
    local count = storage.get("enable_count", 0) + 1
    storage.set("enable_count", count)
    log.info("Enabled " .. count .. " times")
end)
```

Enable, disable and re-enable the plugin. The console count increases. Keep the plugin at the same path to reuse its storage.

## Cancellable timer

```lua
plugin = { api_version = 1, name = "Heartbeat", version = "1.0" }
local heartbeat

event.plugin_enable:set(function()
    heartbeat = timer.every(30, function()
        log.info("Players: " .. players.count())
    end)
end)

event.plugin_disable:set(function()
    if heartbeat then timer.cancel(heartbeat) end
end)
```

Enable the plugin and check the console after 30 seconds. The timer runs on the plugin queue; disabling clears all remaining timers.
