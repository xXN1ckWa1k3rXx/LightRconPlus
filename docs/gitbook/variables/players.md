# players

Look up [player references](player-reference.md) from the session cache. Names are matched exactly.

## get

```lua
players.get(name)
```

Find a player in the current cache.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |

**Returns:** `player reference` or `nil`.

```lua
local p = players.get("Player001")
if p then p.say("Hello") end
```

## list

```lua
players.list()
```

List players currently in the cache.

**Returns:** `table`: one-based array of player references.

```lua
for _, p in ipairs(players.list()) do
    log.info(p.get_name())
end
```

## count

```lua
players.count()
```

Read the number of cached players.

**Returns:** `number`.
