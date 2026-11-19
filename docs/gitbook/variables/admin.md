# admin

Server administration commands. Scope values are `"all"`, `"player"`, `"team"` and `"squad"`. All functions return protocol response words.

## say

```lua
admin.say(scope, text[, target, squad])
```

Send a chat message.

| Name | Type | Description |
| --- | --- | --- |
| scope | string | Message audience. |
| text | string | Message text. |
| target | string / number | Required for player, team and squad scopes: player name or team ID. |
| squad | number | Required for squad scope: squad ID. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

```lua
admin.say("all", "Welcome")
admin.say("player", "Hello", "Player001")
admin.say("squad", "Group up", 1, 2)
```

## yell

```lua
admin.yell(scope, text, seconds[, target, squad])
```

Send a screen announcement.

| Name | Type | Description |
| --- | --- | --- |
| scope | string | Same scope values as say. |
| text | string | Announcement text. |
| seconds | number | Display duration in seconds, subject to game limits. |
| target | string / number | Player name or team ID when required by scope. |
| squad | number | Squad ID for squad scope. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

```lua
admin.yell("all", "Round starts soon", 5)
```

## kick

```lua
admin.kick(name[, reason])
```

Kick a player.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |
| reason | string | Optional; defaults to an empty string. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## kill

```lua
admin.kill(name)
```

Kill a player.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## move

```lua
admin.move(name, team, squad[, force])
```

Move a player to a team and squad.

| Name | Type | Description |
| --- | --- | --- |
| name | string | Exact player name. |
| team | number | Destination team ID. |
| squad | number | Destination squad ID. |
| force | boolean | Optional; defaults to false. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## listPlayers

```lua
admin.listPlayers(...)
```

Forward arguments to `admin.listPlayers` without parsing the response.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## kickPlayer

```lua
admin.kickPlayer(...)
```

Forward arguments to `admin.kickPlayer` without parsing the response.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## killPlayer

```lua
admin.killPlayer(...)
```

Forward arguments to `admin.killPlayer` without parsing the response.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## movePlayer

```lua
admin.movePlayer(...)
```

Forward arguments to `admin.movePlayer` without parsing the response.

| Name | Type | Description |
| --- | --- | --- |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
