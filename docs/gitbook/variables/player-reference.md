# player-reference

A table of methods created by `players.get`, `players.list` or a player event. Methods read the current session cache each time. Use **dot syntax**; these methods do not take an implicit `self` argument.

```lua
local p = players.get("Player001")
if p then
    log.info(p.get_name())
end
```

A reference captures a name, not a permanent player identity. If a name is reused, its getters resolve the currently cached player with that name.

## get_name

```lua
p.get_name()
```

The captured name. It remains available after the player leaves.

**Returns:** `string`.

## get_guid

```lua
p.get_guid()
```

Current EA GUID, or an empty string if unavailable.

**Returns:** `string`.

## get_pb_guid

```lua
p.get_pb_guid()
```

Current PunkBuster GUID, or an empty string if unavailable.

**Returns:** `string`.

## get_ip

```lua
p.get_ip()
```

Current IP, or an empty string if unavailable.

**Returns:** `string`.

## get_team

```lua
p.get_team()
```

Current team ID, or 0 if unavailable.

**Returns:** `number`.

## get_squad

```lua
p.get_squad()
```

Current squad ID, or 0 if unavailable.

**Returns:** `number`.

## get_kills

```lua
p.get_kills()
```

Cached kill count, or 0 if unavailable.

**Returns:** `number`.

## get_deaths

```lua
p.get_deaths()
```

Cached death count, or 0 if unavailable.

**Returns:** `number`.

## get_ping

```lua
p.get_ping()
```

Cached ping, or nil if unavailable.

**Returns:** `number / nil`.

## is_alive

```lua
p.is_alive()
```

Known life state, or nil when unknown or the player is absent.

**Returns:** `boolean / nil`.

## kick

```lua
p.kick([reason])
```

Kick the referenced player.

| Name | Type | Description |
| --- | --- | --- |
| reason | string | Optional; defaults to an empty string. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## kill

```lua
p.kill()
```

Kill the referenced player.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## move

```lua
p.move(team, squad[, force])
```

Move the referenced player.

| Name | Type | Description |
| --- | --- | --- |
| team | number | Team ID. |
| squad | number | Squad ID. |
| force | boolean | Optional; defaults to false. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## say

```lua
p.say(text)
```

Send chat to this player.

| Name | Type | Description |
| --- | --- | --- |
| text | string | Message text. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## yell

```lua
p.yell(text, seconds)
```

Send a screen announcement to this player.

| Name | Type | Description |
| --- | --- | --- |
| text | string | Message text. |
| seconds | number | Display duration in seconds. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
