# spectators

Manage spectator names. Command support depends on the game; a wrapper can exist even if a game rejects its command.

## list

```lua
spectators.list()
```

Read cached names.

**Returns:** `table`: one-based array of names.

## add

```lua
spectators.add(name)
```

Add a name.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## remove

```lua
spectators.remove(name)
```

Remove a name.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
spectators.clear()
```

Clear the live list.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
spectators.save()
```

Save the list on the server.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## load

```lua
spectators.load()
```

Request the saved list; unavailable on some games.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
