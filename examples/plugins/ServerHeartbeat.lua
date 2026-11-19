plugin = {
    api_version = 1,
    name = "Server heartbeat",
    version = "1.0",
    author = "LightRcon+",
    description = "Log a server snapshot at a configurable interval and persist the startup count."
}

settings.define("Heartbeat|Seconds", "int", 60)

event.plugin_enable:set(function()
    local starts = storage.get("starts", 0) + 1
    storage.set("starts", starts)
    log.info("Plugin startup " .. starts)
    timer.every(settings.get("Heartbeat|Seconds"), function()
        local info = server.get_info()
        log.info(info.name .. ": " .. players.count() .. " players on " .. info.map)
    end)
end)
