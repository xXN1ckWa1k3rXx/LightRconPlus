# reserved_slots

Manage reserved-player names. Command support depends on the game; a wrapper can exist even if a game rejects its command.

## list

```lua
reserved_slots.list()
```

Read cached names.

**Returns:** `table`: one-based array of names.

## add

```lua
reserved_slots.add(name)
```

Add a name.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## remove

```lua
reserved_slots.remove(name)
```

Remove a name.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
reserved_slots.clear()
```

Clear the live list.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
reserved_slots.save()
```

Save the list on the server.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## load

```lua
reserved_slots.load()
```

Request the saved list; unavailable on some games.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
