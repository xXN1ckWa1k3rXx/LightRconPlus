# maps

Map rotation helpers. List data is cached; changes are server commands. Save explicitly to persist changes to the server map file.

## list

```lua
maps.list()
```

Read the cached rotation.

**Returns:** `table`: one-based entries with name (raw map ID), mode (raw mode ID) and rounds (number).

## add

```lua
maps.add(name, mode, rounds)
```

Append a rotation entry.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Raw map identifier. |
| mode | string | Raw mode identifier. |
| rounds | number | Round count. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## remove

```lua
maps.remove(index)
```

Remove an entry.

| Name | Type | Description |
| --- | --- | --- |
| index | number | Zero-based protocol index. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
maps.clear()
```

Clear the live rotation.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
maps.save()
```

Save the rotation on the game server.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## load

```lua
maps.load()
```

Load the saved server rotation.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## set_next

```lua
maps.set_next(index)
```

Select the next map without immediately running it.

| Name | Type | Description |
| --- | --- | --- |
| index | number | Zero-based protocol index. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

```lua
maps.set_next(0)
-- server.next_round() -- Switch immediately only when intended.
```
