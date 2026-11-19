# reservedSlotsList

Raw protocol wrappers. These return response words, not the parsed objects returned by convenience namespaces. Unsupported commands or invalid arguments raise server errors. See [Protocol commands](../guides/protocol.md).

## list

```lua
reservedSlotsList.list(...)
```

Forward to `reservedSlotsList.list`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## add

```lua
reservedSlotsList.add(...)
```

Forward to `reservedSlotsList.add`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## remove

```lua
reservedSlotsList.remove(...)
```

Forward to `reservedSlotsList.remove`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
reservedSlotsList.clear(...)
```

Forward to `reservedSlotsList.clear`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
reservedSlotsList.save(...)
```

Forward to `reservedSlotsList.save`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## aggressiveJoin

```lua
reservedSlotsList.aggressiveJoin(...)
```

Forward to `reservedSlotsList.aggressiveJoin`. Arguments and response layout follow the game protocol. No arguments reads the value; pass a boolean to write it.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
