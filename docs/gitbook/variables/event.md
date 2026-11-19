# event

Callback registration. See [Events](../events.md) for payloads and the complete named event list.

## set

```lua
event.<name>:set(callback_or_nil)
```

Register or replace the single callback for a named slot. Passing nil removes it.

| Name | Type | Description |
| --- | --- | --- |
| callback_or_nil | function / nil | Receives the payload documented on the Events page. |

**Returns:** nil.

```lua
event.player_respawn:set(function(self)
    log.info(self.get_name())
end)
```

## on

```lua
event.on(raw_name, callback)
```

Register a callback for an exact raw RCON event name. Replaces the previous callback for that key.

| Name | Type | Description |
| --- | --- | --- |
| raw_name | string | Case-sensitive protocol event name. |
| callback | function | Receives a game-event payload. |

**Returns:** nil.

```lua
event.on("player.onTeamChange", function(self)
    log.info(self.get_name())
end)
```

## off

```lua
event.off(raw_name)
```

Remove a registered raw-name callback. A missing key is ignored.

| Name | Type | Description |
| --- | --- | --- |
| raw_name | string | The key used by event.on. |

**Returns:** nil.
