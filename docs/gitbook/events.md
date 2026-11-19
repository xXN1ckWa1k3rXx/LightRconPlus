# Events

Register a callback with `event.<name>:set(callback)`. Each event slot holds one callback; setting it again replaces the previous callback. Pass `nil` to remove it. Callback return values are ignored.

```lua
event.player_respawn:set(function(self)
    admin.yell("all", self.get_name() .. " has respawned", 5)
end)

-- Remove the callback:
-- event.player_respawn:set(nil)
```

Raw event names can also be registered using `event.on(raw_name, callback)` and removed with `event.off(raw_name)`. Named callbacks run first, then the exact raw-name callback, then `event.raw`. An error interrupts the remaining callbacks for that event.

## Common game-event fields

| Name | Type | Description |
| --- | --- | --- |
| event | string | Exact RCON event name. |
| words | table | Raw protocol words; words[1] is the event name. |
| name | string | Raw words[2], or an empty string; meaningful as a player name only for player events. |
| player | player reference | Reference constructed from words[2]; use only for a player event. |

Player event payloads also expose the [player reference methods](variables/player-reference.md) directly, such as `self.get_name()`. Getters use the current session cache. After a player leaves, identifiers may be empty and numeric fields may be zero. For non-player events, use `self.words` rather than player methods.

## Event list

## player_respawn

A player spawns or respawns.

**RCON source:** `player.onSpawn`, `player.spawn`.

Includes the common game-event fields.

```lua
event.player_respawn:set(function(self)
    log.info(self.get_name() .. ": player_respawn")
end)
```

## player_join

A player joins the server.

**RCON source:** `player.onJoin`.

Includes the common game-event fields.

```lua
event.player_join:set(function(self)
    log.info(self.get_name() .. ": player_join")
end)
```

## player_leave

A player leaves the server.

**RCON source:** `player.onLeave`.

Includes the common game-event fields.

```lua
event.player_leave:set(function(self)
    log.info(self.get_name() .. ": player_leave")
end)
```

## player_chat

A player or the server sends a chat message.

**RCON source:** `player.onChat`.

Includes the common game-event fields.

| Name | Type | Description |
| --- | --- | --- |
| message | string | Chat text from words[3]. |
| scope | string | Raw chat scope from words[4]. |

```lua
event.player_chat:set(function(self)
    log.info(self.get_name() .. ": player_chat")
end)
```

## player_kill

The server reports a kill.

**RCON source:** `player.onKill`.

Includes the common game-event fields.

| Name | Type | Description |
| --- | --- | --- |
| killer | player reference | Reference for words[2]; an empty name can represent a non-player killer. |
| victim | player reference | Reference for words[3]. |
| weapon | string | Raw weapon identifier from words[4]. |
| headshot | boolean | True when words[5] is "true" (case-insensitive). |

```lua
event.player_kill:set(function(self)
    log.info(self.get_name() .. ": player_kill")
end)
```

## player_authenticated

A player is authenticated.

**RCON source:** `player.onAuthenticated`.

Includes the common game-event fields.

```lua
event.player_authenticated:set(function(self)
    log.info(self.get_name() .. ": player_authenticated")
end)
```

## player_disconnect

The server reports a player disconnect.

**RCON source:** `player.onDisconnect`.

Includes the common game-event fields.

```lua
event.player_disconnect:set(function(self)
    log.info(self.get_name() .. ": player_disconnect")
end)
```

## player_team_change

A player changes team.

**RCON source:** `player.onTeamChange`.

Includes the common game-event fields.

```lua
event.player_team_change:set(function(self)
    log.info(self.get_name() .. ": player_team_change")
end)
```

## player_squad_change

A player changes squad.

**RCON source:** `player.onSquadChange`.

Includes the common game-event fields.

```lua
event.player_squad_change:set(function(self)
    log.info(self.get_name() .. ": player_squad_change")
end)
```

## round_over

A round ends.

**RCON source:** `server.onRoundOver`.

Includes the common game-event fields.

```lua
event.round_over:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```

## level_loaded

A level finishes loading.

**RCON source:** `server.onLevelLoaded`.

Includes the common game-event fields.

```lua
event.level_loaded:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```

## level_loading

The server starts loading a level.

**RCON source:** `server.onLoadingLevel`.

Includes the common game-event fields.

```lua
event.level_loading:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```

## round_over_players

The server publishes end-of-round player data.

**RCON source:** `server.onRoundOverPlayers`.

Includes the common game-event fields.

```lua
event.round_over_players:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```

## round_over_team_scores

The server publishes end-of-round team scores.

**RCON source:** `server.onRoundOverTeamScores`.

Includes the common game-event fields.

```lua
event.round_over_team_scores:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```

## punkbuster_message

PunkBuster sends a message.

**RCON source:** `punkBuster.onMessage`.

Includes the common game-event fields.

```lua
event.punkbuster_message:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```

## plugin_enable

The plugin is enabled after saved settings are restored.

The callback receives a [server info snapshot](variables/server.md#server-info), rather than the common game-event payload.

```lua
event.plugin_enable:set(function(self)
    log.info("plugin_enable: " .. self.name)
end)
```

## plugin_disable

An enabled plugin is being unloaded.

The callback receives a [server info snapshot](variables/server.md#server-info), rather than the common game-event payload.

```lua
event.plugin_disable:set(function(self)
    log.info("plugin_disable: " .. self.name)
end)
```

## setting_changed

An enabled plugin setting is changed.

The callback receives the fields below. The value is a string, including for numeric, boolean and array settings.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Full setting name, including any group prefix. |
| value | string | Serialized new value; use settings.get(name) for a typed value. |

```lua
event.setting_changed:set(function(self)
    log.info(self.name .. " = " .. self.value)
end)
```

## raw

Any server event reaches the plugin.

Includes the common game-event fields. This slot receives unknown event names too.

```lua
event.raw:set(function(self)
    log.info(table.concat(self.words, " | "))
end)
```
