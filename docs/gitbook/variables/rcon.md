# rcon

Raw command access. See [Protocol commands](../guides/protocol.md) for responses, indexes and restrictions.

## command

```lua
rcon.command(command, ...)
```

Send a command through the current session.

| Name | Type | Description |
| --- | --- | --- |
| command | string | Exact RCON command name. |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

```lua
local result = rcon.command("serverInfo")
log.info(result[2] or "Unknown server")
```
