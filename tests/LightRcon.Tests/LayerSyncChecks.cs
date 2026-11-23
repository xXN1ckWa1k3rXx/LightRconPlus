using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class LayerSyncChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var store = new ProfileStore(Path.Combine(root, "layer-sync"));
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        // A large persisted archive must not delay authentication/state requests.
        var directory = store.ServerDirectory(profile.Id);
        using (var history = File.CreateText(Path.Combine(directory, "activity-2026-11-23.jsonl")))
            for (var index = 1; index <= 6000; index++)
                history.WriteLine(JsonSerializer.Serialize(new ActivityEntry(index, DateTimeOffset.Now, "Chat", "Archive", "all", new string('x', 1500))));
        await using var host = new ServerSession(profile, store, pluginDirectory: Path.Combine(root, "empty-sync-library"));
        await host.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!host.IsConnected) await Task.Delay(20, timeout.Token);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        profile.Layer.Port = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop(); profile.Layer.BindAddress = "127.0.0.1";
        await host.SaveLayerAccountAsync(false, "sync", "Sync-test-password", true, LayerPermission.All);
        await host.StartLayerAsync();
        using (var client = new TcpClient { NoDelay = true, ReceiveBufferSize = 4096 })
        {
            await client.ConnectAsync(IPAddress.Loopback, profile.Layer.Port, timeout.Token);
            using var tls = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 }, timeout.Token);
            using var gate = new SemaphoreSlim(1);
            await LayerWire.WriteAsync(tls, new() { Type = "login", Username = "sync", Password = "Sync-test-password" }, gate, timeout.Token);
            await LayerWire.WriteAsync(tls, new() { Type = "session.snapshot", Id = 77, Words = ["state"] }, gate, timeout.Token);
            var welcome = await LayerWire.ReadAsync(tls, timeout.Token);
            check(welcome.SupportsSnapshots, "Layer advertises snapshot support without changing protocol 1 authentication");
            var received = new List<long>();
            var framesBeforeResponse = -1;
            while (received.Count < 6000 || framesBeforeResponse < 0)
            {
                var frame = await LayerWire.ReadAsync(tls, timeout.Token);
                if (frame.Type == "history") received.AddRange(frame.Entries.Select(entry => entry.Sequence));
                if (frame.Type == "response" && frame.Id == 77)
                {
                    framesBeforeResponse = received.Count;
                    check(frame.Snapshot is { Players.Length: > 0, Settings.Count: > 0 }, "one Layer state response includes host players, maps, settings and game capabilities");
                }
            }
            check(framesBeforeResponse < 6000, "Layer answers state requests before a large history replay finishes");
            check(received.SequenceEqual(Enumerable.Range(1, 6000).Select(index => (long)index)), "priority responses preserve every historical entry in sequence");
        }
        // Remove the archive to isolate login RCON counts from replay work and host polling.
        File.Delete(Path.Combine(directory, "activity-2026-11-23.jsonl"));
        var downstream = new ServerProfile { Host = "127.0.0.1", Port = profile.Layer.Port, Username = "sync", ProtectedPassword = SecretProtection.Protect("Sync-test-password") };
        await using var layer = new ServerSession(downstream, new ProfileStore(Path.Combine(root, "layer-sync-client")));
        var before = mock.Received.Count(words => words[0] is "version" or "admin.help" or "admin.listPlayers" or "player.isAlive" or "mapList.list");
        await layer.StartAsync();
        while (!layer.IsConnected) await Task.Delay(20, timeout.Token);
        var after = mock.Received.Count(words => words[0] is "version" or "admin.help" or "admin.listPlayers" or "player.isAlive" or "mapList.list");
        check(after == before && layer.PlayerSnapshot().Length == host.PlayerSnapshot().Length && layer.MapSnapshot().SequenceEqual(host.MapSnapshot()),
            "snapshot login avoids upstream version, help, player, life-state and map queries");
        await layer.ApplySettingsAsync(new() { ["vars.serverName"] = "Written through Layer" }, timeout.Token);
        check(host.SettingsSnapshot()["vars.serverName"] == "Written through Layer" && profile.DesiredSettings["vars.serverName"] == "Written through Layer"
            && mock.Received.Any(words => words.SequenceEqual(new[] { "vars.serverName", "Written through Layer" })),
            "Layer settings writes execute on the host RCON connection and update its persisted desired value");
    }
}
