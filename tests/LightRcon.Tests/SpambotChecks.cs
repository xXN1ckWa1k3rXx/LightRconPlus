using System.IO.Compression;
using System.Net;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class SpambotChecks
{
    private static readonly string[] Messages = [
        "Welcome to ZAP server! Our QQ group: 1079977407 - discord: type !discord in chat.",
        "NOTICE: New rcon tool is testing on server, any problem is expected.",
        "Discord server: https://discord.gg/8d8kGGat7Q - type !discord show again",
        "Type /help to get more infomation",
        "Griefers/Disturbing others will be warned; a level 3 warning will result in a 24-hour ban.",
        "You can use ! or / to use commands",
        "Someone abuse? Use /report to contact server owner"
    ];

    public static async Task Run(string root, Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP") is not { } package) return;
        var library = Directory.CreateDirectory(Path.Combine(root, "spambot-library")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(library, "BF4")).FullName;
        var path = Path.Combine(folder, "CSpambot.cs");
        var peerPath = Path.Combine(folder, "SpambotPeer.cs");
        File.WriteAllText(peerPath, """
            using System.Collections.Generic;
            using PRoCon.Core;
            using PRoCon.Core.Plugin;
            namespace PRoConEvents {
                public class SpambotPeer : PRoConPluginAPI, IPRoConPluginInterface {
                    private string text = "before";
                    public string GetPluginName() { return "Spambot peer"; }
                    public string GetPluginVersion() { return "1"; }
                    public string GetPluginAuthor() { return "Test"; }
                    public string GetPluginWebsite() { return ""; }
                    public string GetPluginDescription() { return ""; }
                    public void OnPluginLoaded(string host, string port, string version) { }
                    public void OnPluginEnable() { }
                    public void OnPluginDisable() { }
                    public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable> { new CPluginVariable("Text", typeof(string), text) }; }
                    public List<CPluginVariable> GetDisplayPluginVariables() { return GetPluginVariables(); }
                    public void SetPluginVariable(string name, string value) { if (name == "Text") text = value; }
                }
            }
            """);
        using (var zip = ZipFile.OpenRead(package))
            foreach (var entry in zip.Entries.Where(entry => entry.Name.StartsWith("CSpambot", StringComparison.Ordinal)))
            {
                if (entry.FullName == "Plugins/CSpambot.inc") entry.ExtractToFile(Path.Combine(library, entry.Name));
                else if (entry.FullName.StartsWith("Plugins/BF4/", StringComparison.Ordinal)) entry.ExtractToFile(Path.Combine(folder, entry.Name));
            }
        if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_SPAMBOT_DLL") is { } binary)
            File.Copy(binary, Path.ChangeExtension(path, ".dll"));
        var store = new ProfileStore(Path.Combine(root, "spambot-store"));
        File.Copy(package, Path.Combine(store.Root, "Procon-v1.5.4.2.zip"));
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        await using var session = new ServerSession(profile, store, pluginDirectory: library);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        async Task Wait(Func<bool> predicate) { while (!predicate()) await Task.Delay(25, timeout.Token); }
        await session.StartAsync();
        await Wait(() => session.IsConnected && !session.Plugins.IsRestoring && session.Plugins.Snapshot().Any(item => item.Path == path && item.Kind == "Procon"));
        var settings = await session.Plugins.GetSettingsAsync(path);
        var messages = settings.Single(item => item.Label == "Messages");
        var interval = settings.Single(item => item.Label == "Interval between messages (seconds)");
        await session.Plugins.SetSettingAsync(path, messages.Name, string.Join('|', Messages.Select(WebUtility.UrlEncode)));
        await session.Plugins.SetSettingAsync(path, interval.Name, "1");
        var saved = (await session.Plugins.GetSettingsAsync(path)).Single(item => item.Name == messages.Name);
        check(saved.Value.Split('|').Select(WebUtility.UrlDecode).SequenceEqual(Messages), "CSpambot stores all seven supplied announcement lines without changing their contents");
        var from = mock.Received.Count;
        string[] Sent() => mock.Received.Skip(from).Where(words => words[0] == "admin.say" && words.Length > 1 && Messages.Contains(words[1])).Select(words => words[1]).ToArray();
        await session.Plugins.EnableAsync(path);
        await Wait(() => Sent().Length >= 15);
        Console.WriteLine("Spambot message indices: " + string.Join(",", Sent().Select(text => Array.IndexOf(Messages, text) + 1)));
        check(Sent().Take(15).SequenceEqual(Enumerable.Range(0, 15).Select(index => Messages[index % Messages.Length])), "Idle CSpambot rotates through seven lines for two complete cycles before returning to the first line");
        await Wait(() => Sent().Length >= 17);
        var before = Sent().Length;
        // The Procon codec uses lowercase percent escapes; the editor uses uppercase.
        // Leaving the Messages field must not reapply unchanged lines and restart tasks.
        await session.Plugins.SetSettingAsync(path, messages.Name, string.Join('|', Messages.Select(WebUtility.UrlEncode)));
        await session.Plugins.SetSettingAsync(peerPath, "Text", "after");
        await Wait(() => Sent().Length > before);
        Console.WriteLine("Spambot indices after changing peer: " + string.Join(",", Sent().Select(text => Array.IndexOf(Messages, text) + 1)));
        check(Sent().Skip(before).First() == Messages[before % Messages.Length], "Saving unchanged announcement lines before editing another plugin preserves CSpambot's rotation position");
        await session.Plugins.DisableAsync(path);
        await session.Plugins.SetSettingAsync(path, interval.Name, "120");
        from = mock.Received.Count;
        await session.Plugins.EnableAsync(path);
        await Wait(() => Sent().Length >= 1);
        await session.Plugins.SetSettingAsync(path, messages.Name, string.Join('|', Messages.Select(WebUtility.UrlEncode)));
        await session.Plugins.SetSettingAsync(peerPath, "Text", "another peer edit");
        await Task.Delay(2200, timeout.Token);
        check(Sent().SequenceEqual(Messages.Take(1)) && (await session.Plugins.GetSettingsAsync(path)).Single(item => item.Name == interval.Name).Value == "120",
            "With a 120-second interval, editing another plugin does not immediately repeat the first announcement");
        await session.Plugins.DisableAsync(path);
    }
}
