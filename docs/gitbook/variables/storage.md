# storage

JSON-backed storage isolated by server and absolute plugin path. Keys are hashed into filenames; this namespace does not expose filesystem paths.

## get

```lua
storage.get(key[, default_value])
```

Read a saved value, or the supplied default when no file exists. Malformed stored JSON raises an error.

| Name | Type | Description |
| --- | --- | --- |
| key | string | 1–100 characters. |
| default_value | any | Optional; defaults to nil. |

**Returns:** JSON-compatible Lua value.

```lua
local count = storage.get("count", 0)
storage.set("count", count + 1)
```

## set

```lua
storage.set(key, value)
```

Write a JSON-compatible value atomically.

| Name | Type | Description |
| --- | --- | --- |
| key | string | 1–100 characters. |
| value | nil / boolean / number / string / table | Value encoded using the same rules as json.encode. |

**Returns:** nil.

## remove

```lua
storage.remove(key)
```

Delete a key; a missing key is ignored.

| Name | Type | Description |
| --- | --- | --- |
| key | string | 1–100 characters. |

**Returns:** nil.
