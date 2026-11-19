# timer

Timers run on the plugin queue and are checked approximately every 100 ms. Execution can be delayed by other callbacks. Each plugin may keep at most 128 timers. Disabling clears them.

## after

```lua
timer.after(seconds, callback)
```

Schedule a callback once.

| Name | Type | Description |
| --- | --- | --- |
| seconds | number | Finite interval from 0.1 to 31,536,000 seconds. |
| callback | function | Called without arguments. |

**Returns:** `number`: timer ID.

```lua
local id = timer.after(5, function()
    log.info("Timer fired")
end)
```

## every

```lua
timer.every(seconds, callback)
```

Repeat a callback. Missed intervals are not replayed; the next due time is based on the latest tick.

| Name | Type | Description |
| --- | --- | --- |
| seconds | number | Finite interval from 0.1 to 31,536,000 seconds. |
| callback | function | Called without arguments. |

**Returns:** `number`: timer ID.

```lua
local id = timer.every(5, function()
    log.info("Timer fired")
end)
```

## cancel

```lua
timer.cancel(id)
```

Remove a timer.

| Name | Type | Description |
| --- | --- | --- |
| id | number | ID returned by after or every. |

**Returns:** `boolean`: whether a timer was removed.
