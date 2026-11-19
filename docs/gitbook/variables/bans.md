# bans

Server ban-list helpers. Save changes explicitly. List data is cached.

## list

```lua
bans.list()
```

Read cached bans. The seconds and rounds fields are raw strings.

**Returns:** `table`: entries containing index, id_type, identifier, duration, seconds, rounds and reason.

## add

```lua
bans.add(id_type, identifier, duration[, amount], reason)
```

Forward a ban to the server. For perm omit amount; for seconds or rounds include a positive amount.

| Name | Type | Description |
| --- | --- | --- |
| id_type | string | guid, name or ip. |
| identifier | string | Value for the selected identifier type. |
| duration | string | perm, seconds or rounds. |
| amount | number | Required for seconds and rounds; omit for perm. |
| reason | string | Ban reason; game limits apply. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

```lua
bans.add("name", "Example", "seconds", 1800, "Rule violation")
bans.save()
```

## remove

```lua
bans.remove(id_type, identifier)
```

Remove a ban using its identifier. This forwards the game command, not a numeric list index.

| Name | Type | Description |
| --- | --- | --- |
| id_type | string | guid, name or ip. |
| identifier | string | The identifier to remove. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## clear

```lua
bans.clear()
```

Clear the live ban list.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## save

```lua
bans.save()
```

Save bans on the game server.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## load

```lua
bans.load()
```

Load saved bans on the game server.

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
