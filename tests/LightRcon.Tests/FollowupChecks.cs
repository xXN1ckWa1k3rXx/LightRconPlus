using System.Net;
using System.Net.Sockets;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class FollowupChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> condition)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!condition()) await Task.Delay(20, limit.Token);
        }
        var folder = Directory.CreateDirectory(Path.Combine(root, "followup")).FullName;
        var store = new ProfileStore(Path.Combine(folder, "host"));
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        await using var host = new ServerSession(profile, store, pluginDirectory: Path.Combine(folder, "empty-library"));
        await host.StartAsync(); await Wait(() => host.IsConnected);
        check(Enumerable.Range(1, 4).All(team => profile.DesiredSettings["vars.teamFactionOverride " + team] == "default") && !SettingsCatalog.StartupConfig(profile).Contains("vars.teamFactionOverride"),
            "all four BF4 faction overrides default to omission from Startup.txt without forcing a faction");
        await host.ApplySettingsAsync(new() { ["vars.teamFactionOverride 2"] = "2" });
        check(mock.Factions[1] == 2 && SettingsCatalog.StartupConfig(profile).Split('\n').Where(line => line.StartsWith("vars.teamFactionOverride")).Select(CommandText.Parse).Single().SequenceEqual(new[] { "vars.teamFactionOverride", "2", "2" }),
            "explicit faction override uses separate team/faction words and exports only that team");
        await host.ApplySettingsAsync(new() { ["vars.teamFactionOverride 2"] = "default" });
        check(!SettingsCatalog.StartupConfig(profile).Contains("vars.teamFactionOverride") && !mock.Received.Any(words => words.Contains("default")),
            "returning a faction override to Default omits it and never sends an unsupported Default token to RCON");
        check(!SettingsCatalog.Supports("vars.teamFactionOverride 1", GameId.BF3) && !SettingsCatalog.Supports("vars.teamFactionOverride 1", GameId.BFHL), "faction overrides belong only to BF4");
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); profile.Layer.Port = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        profile.Layer.BindAddress = "127.0.0.1";
        await host.SaveLayerAccountAsync(false, "admin", "Followup-layer-password", true, LayerPermission.All);
        await host.SaveLayerAccountAsync(false, "viewer", "Followup-layer-password", true, LayerPermission.View);
        await host.StartLayerAsync();
        ServerProfile Client(string user) => new() { Host = "127.0.0.1", Port = profile.Layer.Port, Username = user, ProtectedPassword = SecretProtection.Protect("Followup-layer-password") };
        await using var client = new ServerSession(Client("admin"), new ProfileStore(Path.Combine(folder, "client")));
        await using var observer = new ServerSession(Client("viewer"), new ProfileStore(Path.Combine(folder, "observer")));
        await client.StartAsync(); await observer.StartAsync(); await Wait(() => client.IsConnected && observer.IsConnected);
        var changed = new List<MapEntry> { new("MP_Damage", "RushLarge0", 3) };
        await host.ReplaceMapsAsync(changed);
        await Wait(() => observer.MapSnapshot().SequenceEqual(changed) && observer.Profile.DesiredMaps!.SequenceEqual(changed));
        check(true, "host map edits immediately push the visible/current rotation and desired list to other Layer clients");
        changed.Add(new("MP_Abandoned", "ConquestLarge0", 4));
        await client.ReplaceMapsAsync(changed);
        await Wait(() => observer.MapSnapshot().SequenceEqual(changed));
        check(host.MapSnapshot().SequenceEqual(changed) && mock.DiskMaps.SequenceEqual(changed), "Layer map edits execute as one host transaction and push the saved rotation to other clients");
        await using var wire = new LayerConnection(); await wire.ConnectAsync(Client("admin"), default);
        try { await wire.RequestAsync(new() { Type = "maps.replace", Maps = new(0, []), ExpectedMaps = [] }); throw new Exception("Stale rotation accepted"); }
        catch (RconException) { check(mock.DiskMaps.SequenceEqual(changed), "stale map edits are rejected without rolling back another administrator's rotation"); }
        await using var viewer = new LayerConnection(); await viewer.ConnectAsync(Client("viewer"), default);
        try { await viewer.RequestAsync(new() { Type = "maps.replace", Maps = new(0, []), ExpectedMaps = changed.ToArray() }); throw new Exception("Viewer map edit accepted"); }
        catch (RconException) { check(mock.DiskMaps.SequenceEqual(changed), "atomic map edits require Maps permission"); }
        await client.ApplySettingsAsync(new() { ["vars.teamFactionOverride 1"] = "1" });
        check(mock.Factions[0] == 1 && profile.DesiredSettings["vars.teamFactionOverride 1"] == "1", "Layer faction changes use the host connection and persist its team-specific setting");
        try { await viewer.ExecuteAsync(["vars.teamFactionOverride", "1", "2"]); throw new Exception("Viewer faction edit accepted"); }
        catch (RconException) { check(true, "faction writes require Settings permission while reads remain available to viewers"); }
        check((await viewer.ExecuteAsync(["vars.teamFactionOverride"])).Length == 5, "Layer faction reads preserve the four-team RCON response");
        mock.Maps.Clear(); mock.Maps.Add(new("MP_Abandoned", "RushLarge0", 2));
        try { await wire.RequestAsync(new() { Type = "maps.replace", Maps = new(0, []), ExpectedMaps = changed.ToArray() }); throw new Exception("Unpolled rotation edit accepted"); }
        catch (RconException) { check(mock.Maps.Count == 1 && mock.Maps[0].GameMode == "RushLarge0", "atomic map edits check the live RCON rotation even before the host polls an external change"); }
        await host.RefreshAsync(false);
        await Wait(() => observer.MapSnapshot().SequenceEqual(mock.Maps));
        check(true, "lightweight refresh detects external map rotation edits and immediately forwards them");
        await LuaChecks(host, mock, folder, check, Wait);
    }
    private static async Task LuaChecks(ServerSession host, MockFrostbiteServer mock, string folder, Action<bool, string> check, Func<Func<bool>, Task> wait)
    {
        var source = Path.Combine(folder, "Diagnostics.lua");
        File.WriteAllText(source, """
            local function deepest() error([[failure at 'C:\Users\Private Name\secret\config.json']]) end
            local function middle() deepest(); return true end
            event.player_chat:set(function(e)
                if e.message == 'fail' then middle()
                elseif e.message == 'budget' then pcall(function() while true do end end)
                else log.info('recovered ' .. e.message) end
            end)
            event.plugin_enable:set(function()
                local ok, message = pcall(function() json.decode('invalid-json') end)
                assert(not ok and message ~= nil)
                print([[path 'C:\Users\Private Name\data\file.txt']])
                log.info([[path '/home/private/data/file.txt']])
                timer.after(0.1, function() error('timer failure') end)
                timer.after(0.2, function() log.info('healthy timer') end)
            end)
            """);
        await host.Plugins.EnableAsync(source);
        await wait(() => host.ConsoleSnapshot().Any(entry => entry.Text == "healthy timer"));
        check(host.ConsoleSnapshot().Any(entry => entry.Text.Contains("timer failure") && entry.Text.Contains("stack traceback:")), "Lua timer failures print a traceback and leave other timers running");
        await mock.EmitAsync("player.onChat", "DiagnosticsProbe", "fail", "all");
        await wait(() => host.Plugins.Snapshot().Single(plugin => plugin.Path == source).Error.Contains("failure at"));
        var error = host.Plugins.Snapshot().Single(plugin => plugin.Path == source).Error;
        check(error.Contains("Diagnostics.lua:") && error.Contains("stack traceback:") && error.Contains("deepest") && error.Contains("middle") && !error.Contains("Private Name") && !error.Contains(folder),
            "nested Lua exceptions retain script, line and caller names with absolute paths redacted");
        check(!host.ConsoleSnapshot().Where(entry => entry.Kind.StartsWith("Lua") || entry.Text.StartsWith("Diagnostics.lua:")).Any(entry => entry.Text.Contains("Private Name") || entry.Text.Contains("/home/private") || entry.Text.Contains("C:\\Users")),
            "Lua print, log and exception output redact quoted Windows and Unix filesystem paths");
        await mock.EmitAsync("player.onChat", "DiagnosticsProbe", "budget", "all");
        await wait(() => host.Plugins.Snapshot().Single(plugin => plugin.Path == source).Error.Contains("instruction budget"));
        check(true, "Lua instruction budget interrupts runaway code even inside pcall");
        await mock.EmitAsync("player.onChat", "DiagnosticsProbe", "after-error", "all");
        await wait(() => host.ConsoleSnapshot().Any(entry => entry.Text == "recovered after-error"));
        check((await host.ExecuteAsync(["version"])).Length > 1, "Lua event handling and RCON continue after both exceptions and runaway callbacks");
        await host.Plugins.DisableAsync(source);
        var syntax = Path.Combine(folder, "Syntax.lua"); File.WriteAllText(syntax, "local function bad(\ninvalid syntax");
        try { await host.Plugins.ImportAsync(syntax); throw new Exception("Bad Lua compiled"); }
        catch (Exception) when (host.Plugins.Snapshot().Any(plugin => plugin.Path == syntax && plugin.Error.Contains("Syntax.lua:")))
        { check(!host.Plugins.Snapshot().Single(plugin => plugin.Path == syntax).Error.Contains(folder), "Lua syntax errors retain script/line diagnostics without exposing the plugin directory"); }
    }
}
