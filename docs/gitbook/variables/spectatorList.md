# spectatorList

Raw protocol wrappers. These return response words, not the parsed objects returned by convenience namespaces. Unsupported commands or invalid arguments raise server errors. See [Protocol commands](../guides/protocol.md).

## list

```lua
spectatorList.list(...)
```

Forward to `spectatorList.list`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## add

```lua
spectatorList.add(...)
```

Forward to `spectatorList.add`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## remove

```lua
spectatorList.remove(...)
```

Forward to `spectatorList.remove`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
spectatorList.clear(...)
```

Forward to `spectatorList.clear`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
spectatorList.save(...)
```

Forward to `spectatorList.save`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## load

```lua
spectatorList.load(...)
```

Forward to `spectatorList.load`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
