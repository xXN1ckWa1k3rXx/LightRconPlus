using System.Diagnostics;
using System.Reflection;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class ProconBridgeChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP") is not { } archive) return;
        var store = new ProfileStore(Path.Combine(root, "bridge-store"));
        File.Copy(archive, Path.Combine(store.Root, "Procon-v1.5.4.2.zip"));
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        var library = Directory.CreateDirectory(Path.Combine(root, "bridge-library")).FullName;
        await using var session = new ServerSession(profile, store, pluginDirectory: library);
        async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!predicate()) await Task.Delay(25, timeout.Token);
        }
        await session.StartAsync();
        await Wait(() => session.IsConnected && !session.Plugins.IsRestoring);
        var plugin = Path.Combine(library, "BridgeProbe.cs");
        File.WriteAllText(plugin, """
            using System.Collections.Generic;
            using PRoCon.Core;
            using PRoCon.Core.Plugin;
            namespace PRoConEvents {
                public class BridgeProbe : PRoConPluginAPI, IPRoConPluginInterface {
                    public string GetPluginName() { return "Bridge probe"; }
                    public string GetPluginVersion() { return "1"; }
                    public string GetPluginAuthor() { return "Test"; }
                    public string GetPluginWebsite() { return ""; }
                    public string GetPluginDescription() { return "Bridge recovery fixture"; }
                    public void OnPluginLoaded(string host, string port, string version) { RegisterEvents(GetType().Name, "OnGlobalChat"); }
                    public void OnPluginEnable() { }
                    public void OnPluginDisable() { }
                    public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable>(); }
                    public List<CPluginVariable> GetDisplayPluginVariables() { return GetPluginVariables(); }
                    public void SetPluginVariable(string name, string value) { }
                    public void Query(string command) { ExecuteCommand("procon.protected.send", command); }
                    public override void OnGlobalChat(string soldier, string text) {
                        if (soldier == "BridgeTest") ExecuteCommand("procon.protected.send", "admin.say", "bridge " + text, "all");
                    }
                }
            }
            """);
        await session.Plugins.EnableAsync(plugin);
        // Keep the process identity to distinguish survival from an unnoticed restart.
        Process Worker()
        {
            var runtime = session.Plugins.GetType().GetField("procon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session.Plugins)!;
            return (Process)runtime.GetType().GetField("process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        }
        var worker = Worker();
        var query = Path.Combine(root, "BridgeQuery.lua");
        async Task Query(string command)
        {
            File.WriteAllText(query, "event.plugin_enable:set(function() procon.call('BridgeProbe', 'Query', '" + command + "') end)");
            await session.Plugins.EnableAsync(query);
            await session.Plugins.DisableAsync(query);
        }
        async Task AssertEvents(string message)
        {
            await mock.EmitAsync("player.onChat", "BridgeTest", message, "all");
            await Wait(() => mock.Received.Any(words => words.SequenceEqual(new[] { "admin.say", "bridge " + message, "all" })));
            check(ReferenceEquals(Worker(), worker) && !worker.HasExited && session.Plugins.Snapshot().Single(item => item.Path == plugin) is { Enabled: true, Error.Length: 0 },
                "Procon keeps the same enabled worker and processes events after " + message);
        }
        // Clear the rejection as soon as the bridge sees it so normal host polls stay healthy.
        void ResetRejection(ConsoleEntry entry)
        {
            if (entry.Kind == "Procon" && entry.Text == "serverInfo: InvalidArguments") mock.RejectServerInfo = false;
        }
        session.Logged += ResetRejection;
        mock.RejectServerInfo = true;
        await Query("serverInfo");
        await Wait(() => !mock.RejectServerInfo);
        session.Logged -= ResetRejection;
        await AssertEvents("serverInfo rejection");
        var ignored = 0;
        mock.IgnoreResponse = words => words[0] == "serverInfo" && Interlocked.CompareExchange(ref ignored, 1, 0) == 0;
        await Query("serverInfo");
        await Wait(() => session.ConsoleSnapshot().Any(entry => entry.Kind == "Procon" && entry.Text.Contains("serverInfo timed out")));
        mock.IgnoreResponse = null;
        await AssertEvents("serverInfo timeout");
        var notifications = 0;
        session.Plugins.Changed += () => Interlocked.Increment(ref notifications);
        worker.Kill();
        await worker.WaitForExitAsync();
        await mock.EmitAsync("player.onChat", "BridgeTest", "after exit 1", "all");
        await Wait(() => session.Plugins.Snapshot().Single(item => item.Path == plugin) is { Enabled: false, Error.Length: > 0 });
        for (var i = 0; i < 5; i++) await mock.EmitAsync("player.onChat", "BridgeTest", "after exit " + i, "all");
        // An RCON round trip drains the preceding events through the host's read loop.
        await session.ExecuteAsync(["version"]);
        check(session.ConsoleSnapshot().Count(entry => entry.Kind == "Plugin" && entry.Text.StartsWith("BridgeProbe.cs: ")) == 1
            && notifications == 1, "a dead Procon worker reports each plugin once and publishes its disabled state without event log spam");
        await session.Plugins.EnableAsync(plugin);
        worker = Worker();
        await AssertEvents("explicit reload after exit");
    }
}
