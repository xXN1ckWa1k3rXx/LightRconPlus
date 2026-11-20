using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class AdministrationChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!predicate()) await Task.Delay(25, timeout.Token);
        }
        check(GeoLocation.CountryCode("8.8.8.8") == "US" && GeoLocation.CountryCode("::ffff:8.8.8.8") == "US", "offline Geo database resolves public IPv4 and mapped IPv6");
        check(new[] { "", "bad-ip", "127.0.0.1", "192.168.1.1", "10.1.2.3", "172.16.0.1", "100.64.0.1", "::1", "2001:4860:4860::8888" }.All(ip => GeoLocation.CountryCode(ip) == ""), "unknown, local and unsupported IPv6 addresses do not invent a Geo location");
        await using var mock = new MockFrostbiteServer();
        var store = new ProfileStore(Path.Combine(root, "administration"));
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        await using var host = new ServerSession(profile, store);
        await host.StartAsync(); await Wait(() => host.IsConnected);
        await mock.EmitAsync("punkBuster.onMessage", "PunkBuster Server: 1 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa(VALID) 8.8.8.8:3659 OK 1 3.0 0 (W) \"Example_Alpha\"");
        await Wait(() => host.PlayerSnapshot().Any(player => player.Geo == "US"));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); profile.Layer.Port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        profile.Layer.BindAddress = "127.0.0.1";
        const string password = "Layer-test-password";
        await host.SaveLayerAccountAsync(false, "Commander", password, true, LayerPermission.All);
        await host.SaveLayerAccountAsync(false, "Observer", password, true, LayerPermission.View);
        host.Log("Plugin", "plugin output before Layer login");
        host.Log("RCON", "non-plugin console marker");
        await host.StartLayerAsync();
        ServerProfile Client(string name) => new() { Host = "127.0.0.1", Port = profile.Layer.Port, Username = name, ProtectedPassword = SecretProtection.Protect(password) };
        await using var sender = new LayerConnection(); await using var observer = new LayerConnection();
        ConcurrentQueue<ActivityEntry> received = new(); observer.HistoryReceived += entries => { foreach (var entry in entries) received.Enqueue(entry); };
        ConcurrentQueue<ConsoleEntry> console = new(); observer.PluginConsoleReceived += entries => { foreach (var entry in entries) console.Enqueue(entry); };
        await sender.ConnectAsync(Client("Commander"), default); await observer.ConnectAsync(Client("Observer"), default);
        await Wait(() => console.Any(entry => entry.Text == "plugin output before Layer login"));
        host.Log("Procon", "live Procon console marker"); host.Log("Lua warning", "live Lua warning marker");
        await Wait(() => console.Any(entry => entry.Text == "live Lua warning marker"));
        check(console.Any(entry => entry.Text == "live Procon console marker") && console.All(entry => entry.IsPluginConsole) && !console.Any(entry => entry.Text == "non-plugin console marker"), "Layer viewers receive recent and live plugin-only console output including Procon and Lua log levels");
        var layerPlayers = FrostbiteParser.Players(await observer.ExecuteAsync(["admin.listPlayers", "all"]));
        check(layerPlayers.Single(player => player.Name == "Example_Alpha").Geo == "US", "Layer player snapshots include host IP enrichment for Geo without waiting for a new PunkBuster event");
        await sender.ExecuteAsync(["admin.say", "attributed say", "all"]);
        await sender.ExecuteAsync(["admin.yell", "attributed yell", "5", "all"]);
        await Wait(() => received.Any(entry => entry.Text == "attributed yell"));
        foreach (var (body, type) in new[] { ("attributed say", "Say"), ("attributed yell", "Yell") })
        {
            var entries = host.Journal.Snapshot().Where(entry => entry.Text.Contains(body)).ToArray();
            check(entries.Length == 1 && entries[0].Name == "Commander" && entries[0].IsAdminMessage && !entries[0].IsPluginMessage && entries[0].AdminMessageType == type && entries[0].Text == body,
                "Layer " + type + " uses authenticated username once in host history despite server echoes");
            check(received.Single(entry => entry.Text == body).Name == "Commander" && !received.Single(entry => entry.Text == body).IsPluginMessage && mock.Received.Any(words => words.Length > 1 && words[1] == "[Commander] " + body),
                "Layer " + type + " preserves username across other clients and the in-game message");
        }
        await host.ExecuteAsync(["admin.say", "manual-host-chat", "all"]);
        check(!host.Journal.Snapshot().Single(entry => entry.Text == "manual-host-chat").IsPluginMessage, "direct host Chat messages remain visible with plugin messages hidden");
        var plugin = Path.Combine(root, "ChatOrigin.lua");
        File.WriteAllText(plugin, "event.plugin_enable:set(function() admin.say('all', 'plugin-origin-say'); admin.yell('all', 'plugin-origin-yell', 5) end)");
        await host.Plugins.EnableAsync(plugin);
        await Wait(() => received.Any(entry => entry.Text == "plugin-origin-yell"));
        check(new[] { "plugin-origin-say", "plugin-origin-yell" }.All(body => host.Journal.Snapshot().Single(entry => entry.Text == body).IsPluginMessage && received.Single(entry => entry.Text == body).IsPluginMessage), "plugin Say/Yell source metadata survives host journaling and Layer delivery");
        await host.Plugins.DisableAsync(plugin);
        try { await observer.ExecuteAsync(["admin.say", "forbidden", "all"]); throw new Exception("Unauthorized chat accepted"); }
        catch (RconException) { check(!mock.Received.Any(words => words.Contains("[Observer] forbidden")), "View-only Layer users cannot send attributed admin chat"); }
        await host.EndRoundAsync(2);
        check(mock.Received.Any(words => words.SequenceEqual(new[] { "mapList.endRound", "2" })), "force End round uses Procon mapList.endRound with the selected winner");
        await sender.ExecuteAsync(["mapList.endRound", "1"]);
        try { await observer.ExecuteAsync(["mapList.endRound", "2"]); throw new Exception("Unauthorized end round accepted"); }
        catch (RconException) { check(true, "Layer End round requires Maps permission"); }
        foreach (var invalid in new[] { 0, -1, 3 })
        {
            try { await host.EndRoundAsync(invalid); throw new Exception("Invalid winner accepted"); }
            catch (ArgumentException) { check(true, "End round rejects invalid team " + invalid + " before issuing RCON"); }
        }
        mock.TeamScores = ["10", "9", "8", "7"]; await host.RefreshAsync();
        await host.EndRoundAsync(4);
        check(mock.Received.Any(words => words.SequenceEqual(new[] { "mapList.endRound", "4" })), "four-team modes accept team four as the winning team");
        var replay = new ActivityJournal(store.ServerDirectory(profile.Id), true).Replay().SelectMany(entries => entries).Where(entry => entry.Text == "attributed say").ToArray();
        check(replay.Length == 1 && replay[0].Name == "Commander" && replay[0].IsAdminMessage && !replay[0].IsPluginMessage, "persisted admin history retains Layer attribution without classifying manual chat as plugin output");
        check(new ActivityJournal(store.ServerDirectory(profile.Id), true).Replay().SelectMany(entries => entries).Single(entry => entry.Text == "plugin-origin-yell").IsPluginMessage, "plugin source metadata survives history replay");
    }
}
