# Protocol commands

Command functions accept separate protocol words. Do not concatenate an entire command line:

```lua
local result = rcon.command("admin.say", "Server maintenance soon", "all")
assert(result[1] == "OK")
```

Numbers use invariant decimal formatting. Booleans become `"true"` or `"false"`. Invalid argument types raise errors.

All command wrappers return one-based response arrays and raise errors when the server rejects a command. The game determines supported arguments and values; BF3, BF4 and BFHL have differences. Login, event subscription and RCON passwords are managed by LightRcon+ and are not plugin commands.

## Indexes

```lua
local rotation = maps.list()
if #rotation > 0 then
    maps.set_next(0) -- First protocol map index, not Lua array index 1.
end
```

`bans.list()` returns entries whose `index` is a protocol index. Removal uses an identifier type and identifier, not that index:

```lua
for _, ban in ipairs(bans.list()) do
    if ban.id_type == "name" and ban.identifier == "Example" then
        bans.remove(ban.id_type, ban.identifier)
        bans.save()
    end
end
```

## Cached getters and raw responses

`players.list()`, `maps.list()`, `bans.list()`, `reserved_slots.list()` and `spectators.list()` read session caches. Command wrappers such as `admin.listPlayers("all")` and `mapList.list()` request fresh protocol responses but do not return parsed player or map objects.

```lua
local words = mapList.list(0)
log.info("Response words: " .. #words)
```

See the namespace pages for the available wrappers. Use `rcon.command` for a command without a dedicated wrapper, subject to session restrictions.
