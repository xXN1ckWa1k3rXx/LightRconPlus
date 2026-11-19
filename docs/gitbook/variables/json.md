# json

Convert between Lua values and JSON. JSON null becomes nil; null elements can therefore create holes in a decoded Lua array. Lua numbers use double precision.

## encode

```lua
json.encode(value)
```

Encode primitives or tables. Dense one-based numeric tables become arrays, including an empty table. Other tables become objects. Nesting beyond 32 levels and unsupported values raise errors.

| Name | Type | Description |
| --- | --- | --- |
| value | nil / boolean / number / string / table | Value to serialize. |

**Returns:** `string`.

```lua
local text = json.encode({ enabled = true, names = { "A", "B" } })
```

## decode

```lua
json.decode(text)
```

Parse JSON. Malformed JSON raises an error.

| Name | Type | Description |
| --- | --- | --- |
| text | string | JSON text. |

**Returns:** nil / boolean / number / string / table.

```lua
local value = json.decode('{"enabled":true}')
assert(value.enabled)
```
