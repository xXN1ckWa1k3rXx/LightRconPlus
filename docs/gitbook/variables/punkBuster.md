# punkBuster

Raw protocol wrappers. These return response words, not the parsed objects returned by convenience namespaces. Unsupported commands or invalid arguments raise server errors. See [Protocol commands](../guides/protocol.md).

## isActive

```lua
punkBuster.isActive(...)
```

Forward to `punkBuster.isActive`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## pb_sv_command

```lua
punkBuster.pb_sv_command(...)
```

Forward to `punkBuster.pb_sv_command`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
