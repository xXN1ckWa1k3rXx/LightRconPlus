# server

Session state and round management.

## get_info

```lua
server.get_info()
```

Return a new table with the cached server information.

**Returns:** `table`: fields described under Server info.

## get_settings

```lua
server.get_settings()
```

Read the cached server setting values. This is separate from plugin settings.

**Returns:** `table`: command names mapped to raw string values.

```lua
local values = server.get_settings()
log.info(values["vars.serverName"] or "Unknown")
```

## is_connected

```lua
server.is_connected()
```

Read the current session connection state.

**Returns:** `boolean`.

## get_game

```lua
server.get_game()
```

Read the game identifier. Hardline is reported as `"BFHL"`; its plugin folder is named `BFH`.

**Returns:** `string`: "BF3", "BF4" or "BFHL".

## restart_round

```lua
server.restart_round()
```

Send `mapList.restartRound`.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## next_round

```lua
server.next_round()
```

Send `mapList.runNextRound`.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## Server info

| Name | Type | Description |
| --- | --- | --- |
| name | string | Server name. |
| map | string | Raw map identifier. |
| mode | string | Raw game-mode identifier. |
| players | number | Current player count. |
| max_players | number | Player capacity. |
| round | number | Current protocol round index. |
| total_rounds | number | Configured round count. |
| uptime | number | Server uptime in seconds. |
| game | string | BF3, BF4 or BFHL. |
| host | string | Configured connection host. |
| port | number | Configured connection port. |
