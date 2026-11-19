# procon

Interact with the shared Procon compatibility worker. Other than the two local shortcuts below, these services require a loaded Procon plugin. Arbitrary .NET objects are not exposed as Lua userdata.

## execute_command

```lua
procon.execute_command(command, ...)
```

Execute a Procon command. procon.protected.send forwards the remaining words to RCON; procon.protected.pluginconsole.write logs the remaining words locally. Other commands are forwarded to the worker.

| Name | Type | Description |
| --- | --- | --- |
| command | string | Procon command name. |
| ... | string / number / boolean | Separate protocol arguments; numbers and booleans are converted to strings. |

**Returns:** Response word table for procon.protected.send; nil for pluginconsole.write; JSON-compatible worker result otherwise.

```lua
procon.execute_command("procon.protected.send", "admin.say", "Hello", "all")
```

## call

```lua
procon.call(class_name, method, ...)
```

Invoke a method on an enabled Procon plugin. A missing runtime, unavailable method or worker failure raises an error.

| Name | Type | Description |
| --- | --- | --- |
| class_name | string | Procon plugin class name. |
| method | string | Method name. |
| ... | JSON-compatible values | Method arguments. |

**Returns:** JSON-compatible value returned by the worker.
