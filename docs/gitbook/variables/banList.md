# banList

Raw protocol wrappers. These return response words, not the parsed objects returned by convenience namespaces. Unsupported commands or invalid arguments raise server errors. See [Protocol commands](../guides/protocol.md).

## list

```lua
banList.list(...)
```

Forward to `banList.list`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## add

```lua
banList.add(...)
```

Forward to `banList.add`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## remove

```lua
banList.remove(...)
```

Forward to `banList.remove`. Arguments and response layout follow the game protocol. Removal takes an identifier type and value, not a numeric index.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
banList.clear(...)
```

Forward to `banList.clear`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
banList.save(...)
```

Forward to `banList.save`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## load

```lua
banList.load(...)
```

Forward to `banList.load`. Arguments and response layout follow the game protocol.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
