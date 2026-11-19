using LightRcon.Core;
using LightRcon.Core.Plugins;

namespace LightRcon.Tests;

internal static class WorkflowChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var data = Path.Combine(root, "workflow"); var store = new ProfileStore(data);
        check(store.State.ChatLineLimit == 450 && store.State.PluginImportPreference == "ask", "new profiles default to 450 chat lines and asking before imports");
        store.State.ChatLineLimit = 800; store.State.PluginImportPreference = "copy"; store.Save();
        var restored = new ProfileStore(data);
        check(restored.State.ChatLineLimit == 800 && restored.State.PluginImportPreference == "copy", "chat limit and plugin import preference persist");
        store.State.ChatLineLimit = 0; store.State.PluginImportPreference = "bad"; store.Save();
        restored = new ProfileStore(data);
        check(restored.State.ChatLineLimit == 100 && restored.State.PluginImportPreference == "ask", "invalid saved preferences normalize to supported values");
        var package = Path.Combine(root, "sample-package"); Directory.CreateDirectory(Path.Combine(package, "helpers"));
        var source = Path.Combine(package, "Package.lua");
        File.WriteAllText(source, "local helper = require('helpers.message'); plugin={ name=helper.name }; settings.define('Text', 'string', 'before'); event.plugin_enable:set(function() log.info('enabled once') end)");
        File.WriteAllText(Path.Combine(package, "helpers", "message.lua"), "return { name = 'Copied with module' }");
        File.WriteAllText(Path.Combine(package, "dependency.dll"), "copy dependency fixture");
        var library = Path.Combine(root, "library");
        var copied = await PluginLibrary.CopyAsync(source, GameId.BFHL, library);
        check(copied == Path.Combine(library, "BFH", "Package", "Package.lua") && File.Exists(Path.Combine(Path.GetDirectoryName(copied)!, "helpers", "message.lua")) && File.Exists(Path.Combine(Path.GetDirectoryName(copied)!, "dependency.dll")), "copy import uses BFH folder and preserves modules and dependencies");
        check(PluginLibrary.Discover(library, GameId.BFHL).SequenceEqual(new[] { copied }) && PluginLibrary.Discover(library, GameId.BF4).Length == 0, "plugin discovery selects game-specific package entrypoints without importing dependency DLLs");
        try { await PluginLibrary.CopyAsync(source, GameId.BFHL, library); throw new Exception("Existing package overwritten"); }
        catch (IOException) { check(File.ReadAllText(copied) == File.ReadAllText(source), "copy imports preserve existing packages on name collisions"); }
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile); store.Save();
        await using var session = new ServerSession(profile, store);
        await session.StartAsync(); await Wait(() => session.IsConnected);
        await session.Plugins.EnableAsync(copied);
        await session.Plugins.GetDetailsAsync(copied); await session.Plugins.RefreshAsync(); await session.Plugins.EnableAsync(copied);
        check(session.Plugins.Snapshot().Single().Enabled && session.ConsoleSnapshot().Count(entry => entry.Text == "enabled once") == 1, "metadata, refresh and repeated enable retain activation without duplicate callbacks");
        await session.Plugins.SetSettingAsync(copied, "Text", "persisted");
        session.Journal.SetPersistent(true);
        session.Journal.Add("Chat", "Logger", "all", "chat-log-fixture"); session.Journal.Add("Join", "Logger", "", "event-log-fixture");
        var logs = Directory.GetFiles(store.LogsDirectory, "*.log", SearchOption.AllDirectories);
        check(logs.Any(path => Path.GetFileName(path).StartsWith("chat-") && File.ReadAllText(path).Contains("chat-log-fixture")) && logs.Any(path => Path.GetFileName(path).StartsWith("events-") && File.ReadAllText(path).Contains("event-log-fixture")), "direct connections write separate UTF-8 chat and event logs");
        check(logs.All(path => Path.GetRelativePath(store.LogsDirectory, path).Split(Path.DirectorySeparatorChar).Length == 3) && logs.All(path => path.Contains(session.Info.Name)), "log folders group server names, application launch and connection timestamp");
        var directory = Path.GetDirectoryName(logs[0])!;
        await session.StopAsync(); await session.StartAsync(); await Wait(() => session.IsConnected && !session.Plugins.IsRestoring);
        session.Journal.Add("Chat", "Logger", "all", "second-connection");
        var chats = Directory.GetFiles(directory, "chat-*.log");
        check(chats.Length == 2 && chats.Sum(path => File.ReadAllText(path).Split("chat-log-fixture").Length - 1) == 1, "reconnect creates new log files under the same launch without duplicating old chat");
        var savedSettings = File.ReadAllText(Path.Combine(data, "profiles.json"));
        store.ClearAllLogs();
        check(Directory.GetFiles(store.LogsDirectory, "*.log", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(store.ServerDirectory(profile.Id), "activity-*.jsonl").Length == 0 && File.ReadAllText(Path.Combine(data, "profiles.json")) == savedSettings && File.Exists(copied), "clear all logs removes text and replay logs while preserving profiles and plugins");
        session.Journal.Add("Chat", "Logger", "all", "after-clear");
        check(Directory.GetFiles(store.LogsDirectory, "chat-*.log", SearchOption.AllDirectories).Any(path => File.ReadAllText(path).Contains("after-clear")), "active sessions resume logging after clearing all log files");
    }
    private static async Task Wait(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate()) await Task.Delay(25, timeout.Token);
    }
}
