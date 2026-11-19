using LightRcon.Core;
using LightRcon.Core.Plugins;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LightRcon.Tests;

internal static class PluginChecks
{
    public static async Task Run(ServerSession session, MockFrostbiteServer mock, ProfileStore store, string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!predicate()) await Task.Delay(25, timeout.Token);
        }
        var path = Path.Combine(root, "Packaged.lua");
        using var httpServer = new TcpListener(IPAddress.Loopback, 0);
        httpServer.Start();
        using var httpTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var httpHeaders = new List<string>();
        var httpWorker = Task.Run(async () => {
            for (var request = 0; request < 2; request++)
            {
                using var client = await httpServer.AcceptTcpClientAsync(httpTimeout.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var length = 0;
                while (await reader.ReadLineAsync(httpTimeout.Token) is { Length: > 0 } line)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Split(':')[1]);
                    if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)) httpHeaders.Add(line);
                }
                var body = new char[length];
                if (await reader.ReadBlockAsync(body.AsMemory(), httpTimeout.Token) != length) throw new Exception("Incomplete Lua HTTP body");
                if (new string(body) != "payload") throw new Exception("Incorrect Lua HTTP body");
                await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 11\r\nConnection: close\r\n\r\n{\"ok\":true}"), httpTimeout.Token);
            }
        });
        File.WriteAllText(Path.Combine(root, "helper.lua"), "return { label = 'loaded local module' }");
        File.WriteAllText(path, """
            local api = require('lightrcon')
            assert(vars.commander ~= nil and vars.hacker == nil and vars.ctfRoundTimeModifier == nil)
            assert(require('helper').label == 'loaded local module')
            plugin = { api_version = 1, name = 'Packaged API', version = '1.2', author = 'Test author', website = 'https://example.com', description = 'Lua integration probe' }
            settings.define('Messages|Prefix', 'string', 'First')
            settings.define('Messages|Enabled', 'bool', true)
            settings.define('Messages|Count', 'int', 2)
            settings.define('Messages|Mode', 'enum.Mode(One|Two)', 'One')
            settings.define('Info|Read-only', 'string', 'fixed', true)
            settings.define('Info|Array', 'stringarray', { 'A+B', 'two words' })
            event.plugin_enable:set(function()
                local state = storage.get('roundtrip', { count = 0 })
                storage.set('roundtrip', { count = state.count + 1, list = { 'one', 'two' } })
                local info = server.get_info()
                assert(info.game == 'BF4' and players.count() == 3)
                assert(api.players.get('Example_Alpha').get_team() == 1)
                local parsed = json.decode(json.encode({ array = { 'a', 'b' }, enabled = true }))
                assert(parsed.array[2] == 'b' and parsed.enabled)
                assert(settings.get('Info|Array')[1] == 'A+B' and settings.get('Info|Array')[2] == 'two words')
                local response = http.request('POST', 'HTTP_URL', 'payload', { ['Content-Type'] = 'application/json' })
                assert(response.success and response.status == 200 and json.decode(response.body).ok)
                timer.after(0.1, function() admin.say('all', 'timer ' .. storage.get('roundtrip').count) end)
                admin.say('player', 'targeted message', 'Example_Alpha')
                vars.serverName()
            end)
            event.player_respawn:set(function(self)
                admin.yell('squad', settings.get('Messages|Prefix') .. ' ' .. self.get_name(), 5, 1, 2)
            end)
            event.player_kill:set(function(self)
                admin.say('all', self.killer.get_name() .. '/' .. self.victim.get_name() .. '/' .. self.weapon)
            end)
            event.on('player.onTeamChange', function(self) log.info('team event ' .. self.get_name()) end)
            event.setting_changed:set(function(self) log.info('changed ' .. self.name) end)
            event.plugin_disable:set(function() log.info('disabled lifecycle') end)
            """.Replace("HTTP_URL", "http://127.0.0.1:" + ((IPEndPoint)httpServer.LocalEndpoint).Port + "/"));
        await session.Plugins.ImportAsync(path);
        var details = await session.Plugins.GetDetailsAsync(path);
        check(details.Name == "Packaged API" && details.Version == "1.2" && details.Author == "Test author" && !details.Enabled, "Lua metadata loads without enabling");
        var settings = await session.Plugins.GetSettingsAsync(path);
        check(settings.Length == 6 && settings.Single(item => item.Name == "Messages|Mode").Choices.SequenceEqual(new[] { "One", "Two" }), "Lua grouped typed settings and enum choices");
        try { await session.Plugins.SetSettingAsync(path, "Info|Read-only", "bad"); throw new Exception("Read-only accepted"); }
        catch (InvalidOperationException) { check(true, "Lua read-only setting rejected"); }
        try { await session.Plugins.SetSettingAsync(path, "Messages|Count", "NaN"); throw new Exception("Invalid number accepted"); }
        catch (ArgumentException) { check(true, "Lua numeric setting validation"); }
        await session.Plugins.SetSettingAsync(path, "Messages|Prefix", "Saved");
        await session.Plugins.EnableAsync(path);
        await Wait(() => mock.Received.Any(words => words.Contains("timer 1")));
        check(mock.Received.Any(words => words.SequenceEqual(new[] { "admin.say", "targeted message", "player", "Example_Alpha" })), "Lua targeted audience is encoded correctly");
        await mock.EmitAsync("player.onSpawn", "Example_Alpha", "1");
        await Wait(() => mock.Received.Any(words => words.SequenceEqual(new[] { "admin.yell", "Saved Example_Alpha", "5", "squad", "1", "2" })));
        check(true, "Lua saved setting reaches scoped event command");
        await mock.EmitAsync("player.onKill", "Example_Bravo", "Example_Alpha", "U_M416", "true");
        await Wait(() => mock.Received.Any(words => words.Contains("Example_Bravo/Example_Alpha/U_M416")));
        await mock.EmitAsync("player.onTeamChange", "Example_Alpha", "1", "2");
        await Wait(() => session.ConsoleSnapshot().Any(entry => entry.Text == "team event Example_Alpha"));
        check(true, "Lua structured kill payload and raw named callback");
        await session.Plugins.DisableAsync(path);
        check(session.ConsoleSnapshot().Any(entry => entry.Text == "disabled lifecycle"), "Lua disable lifecycle callback");
        check(session.Plugins.Snapshot().Any(plugin => plugin.Path == path && !plugin.Enabled), "disabled plugins remain installed");
        await session.Plugins.EnableAsync(path);
        await Wait(() => mock.Received.Any(words => words.Contains("timer 2")));
        await httpWorker;
        check(httpHeaders.Count == 2 && httpHeaders.All(header => header.Contains("application/json")), "Lua HTTP request body, content headers and JSON response round trip");
        check((await session.Plugins.GetSettingsAsync(path)).Single(setting => setting.Name == "Messages|Prefix").Value == "Saved", "Lua settings and scoped storage survive disable and enable");
        check(new ProfileStore(store.Root).State.Servers[0].PluginSettings[path]["Messages|Prefix"] == "Saved", "plugin settings persist to profile store");
        await session.Plugins.DisableAsync(path);
        var runaway = Path.Combine(root, "Budget.lua");
        File.WriteAllText(runaway, "event.player_respawn:set(function() while true do end end)");
        await session.Plugins.EnableAsync(runaway);
        await mock.EmitAsync("player.onSpawn", "BudgetProbe", "1");
        await Wait(() => session.Plugins.Snapshot().Any(plugin => plugin.Path == runaway && plugin.Error.Contains("instruction budget")));
        await session.Plugins.DisableAsync(runaway);
        check(true, "Lua instruction limit reports failure without blocking disable");
    }
}
