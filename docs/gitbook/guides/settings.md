# Plugin settings

Define settings while the script loads, then read them in callbacks:

```lua
settings.define("Messages|Enabled", "bool", true)
settings.define("Messages|Text", "string", "Welcome")
settings.define("Messages|Mode", "enum.Mode(Say|Yell)", "Say")
settings.define("Messages|Lines", "stringarray", { "First line", "Second line" })

event.player_respawn:set(function(self)
    if settings.get("Messages|Enabled") then
        self.say(settings.get("Messages|Text"))
    end
end)
```

The final `|` separates a setting's group from its label. The desktop renders booleans, enum choices, text and multiline fields. Editable controls save when they lose focus. Use **...** for a larger multiline editor and **Reload** to retrieve current fields.

Values are validated and saved per server. Read-only settings cannot be changed through the desktop or Layer API. Saved values are applied before the enable callback. A live change emits `event.setting_changed`; its `value` is the serialized string, so use `settings.get(self.name)` for the typed value.

See [settings](../variables/settings.md) for types and signatures.
