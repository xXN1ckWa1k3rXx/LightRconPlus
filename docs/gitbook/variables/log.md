# log

Write to the session console. `print(...)` is also redirected to the console. On direct hosts, console entries are included in the local event log.

## info

```lua
log.info(text)
```

Write a message with the Lua info category.

| Name | Type | Description |
| --- | --- | --- |
| text | string / number / boolean | Message; numbers and booleans are converted to strings. |

**Returns:** nil.

## warning

```lua
log.warning(text)
```

Write a message with the Lua warning category.

| Name | Type | Description |
| --- | --- | --- |
| text | string / number / boolean | Message; numbers and booleans are converted to strings. |

**Returns:** nil.

## error

```lua
log.error(text)
```

Write a message with the Lua error category.

| Name | Type | Description |
| --- | --- | --- |
| text | string / number / boolean | Message; numbers and booleans are converted to strings. |

**Returns:** nil.

## debug

```lua
log.debug(text)
```

Write a message with the Lua debug category.

| Name | Type | Description |
| --- | --- | --- |
| text | string / number / boolean | Message; numbers and booleans are converted to strings. |

**Returns:** nil.
