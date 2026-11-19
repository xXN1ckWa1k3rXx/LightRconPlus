plugin = {
    api_version = 1,
    name = "Respawn announcement",
    version = "1.0",
    author = "LightRcon+",
    description = "Announce player respawns with a configurable duration."
}

settings.define("Messages|Enabled", "bool", true)
settings.define("Messages|Seconds", "int", 5)

event.player_respawn:set(function(self)
    if settings.get("Messages|Enabled") then
        admin.yell("all", self.get_name() .. " is respawn", settings.get("Messages|Seconds"))
    end
end)
