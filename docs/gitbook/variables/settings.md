# settings

Define and read plugin settings. Definitions are restored and persisted per server; use the full grouped name when reading a value.

## define

```lua
settings.define(name, type, default_value[, read_only])
```

Declare a setting. Duplicate names and invalid typed defaults raise errors.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Full name, optionally Group\|Label. |
| type | string | A type listed below. |
| default_value | string / number / boolean / table | Default value; stringarray also accepts a one-based table of strings. |
| read_only | boolean | Optional; defaults to false. |

**Returns:** nil.

```lua
settings.define("Messages|Enabled", "bool", true)
settings.define("Messages|Mode", "enum.Mode(Say|Yell)", "Say")
```

## get

```lua
settings.get(name)
```

Read a typed value. An unknown name returns nil.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Full setting name. |

**Returns:** `boolean`, `number`, `table`, `string` or `nil`.

## Types

| Type | Lua result | Notes |
| --- | --- | --- |
| string, multiline | string | Text; multiline supports line breaks. |
| bool, boolean | boolean | Boolean values. |
| int, int32 | number | 32-bit integer. |
| double, float, single | number | Finite numeric value. |
| stringarray | table | Decoded strings; stored as pipe-separated URL-encoded values. |
| onoff, yesno | string | On/Off or Yes/No. |
| enum.Name(A\|B) | string | One of the listed choices; case-sensitive validation. |

After a live change, `event.setting_changed` receives the serialized value. Definitions and saved-value restoration do not trigger that event while the plugin is disabled.
