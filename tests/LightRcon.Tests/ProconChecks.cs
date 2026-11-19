using LightRcon.Core;
using System.IO.Compression;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LightRcon.Tests;

internal static class ProconChecks
{
    public static async Task Run(ServerSession session, MockFrostbiteServer mock, ProfileStore store, string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!predicate()) await Task.Delay(30, timeout.Token);
        }
        var settingsPath = Path.Combine(root, "SettingsProbe.cs");
        File.WriteAllText(settingsPath, """
            using System;
            using System.Collections.Generic;
            using PRoCon.Core;
            using PRoCon.Core.Players;
            using PRoCon.Core.Plugin;
            namespace PRoConEvents {
                public class SettingsProbe : PRoConPluginAPI, IPRoConPluginInterface {
                    private string message = "default";
                    private string loadedHost;
                    private string loadedPort;
                    public string GetPluginName() { return "Settings probe"; }
                    public string GetPluginVersion() { return "2.3"; }
                    public string GetPluginAuthor() { return "Procon author"; }
                    public string GetPluginWebsite() { return "https://example.com"; }
                    public string GetPluginDescription() { return "<h2>Real Procon settings</h2><p>Read from upstream API.</p>"; }
                    public void OnPluginLoaded(string host, string port, string version) { loadedHost = host; loadedPort = port; RegisterEvents(GetType().Name, "OnPlayerSpawned"); }
                    public void OnPluginEnable() { }
                    public void OnPluginDisable() { }
                    public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable> { new CPluginVariable("Message", typeof(string), message) }; }
                    public List<CPluginVariable> GetDisplayPluginVariables() {
                        return new List<CPluginVariable> {
                            new CPluginVariable("Messages|Message", typeof(string), message),
                            new CPluginVariable("Messages|Mode", "enum.Mode(One|Two)", "Two"),
                            new CPluginVariable("Info|Count", typeof(int), 5, true),
                            new CPluginVariable("Info|Lines", typeof(string[]), new [] { "A+B", "two words" }),
                            new CPluginVariable("Info|Host", typeof(string), loadedHost, true),
                            new CPluginVariable("Info|Port", typeof(string), loadedPort, true)
                        };
                    }
                    public void SetPluginVariable(string name, string value) { if (name == "Message") message = value; }
                    public override void OnPlayerSpawned(string soldierName, Inventory inventory) { ExecuteCommand("procon.protected.send", "admin.say", "spawn " + soldierName + " " + message, "all"); }
                    public string Echo(string text) { ExecuteCommand("procon.protected.send", "admin.say", "peer " + text, "all"); return "echo " + text; }
                }
            }
            """);
        await session.Plugins.ImportAsync(settingsPath);
        var details = await session.Plugins.GetDetailsAsync(settingsPath);
        check(details.Name == "Settings probe" && details.Version == "2.3" && details.Author == "Procon author" && details.Description.Contains("<h2>"), "Procon details come from plugin metadata");
        var settings = await session.Plugins.GetSettingsAsync(settingsPath);
        check(settings.Length == 6 && settings.Single(item => item.Name == "Info|Count").ReadOnly && settings.Single(item => item.Name == "Messages|Mode").Choices.SequenceEqual(new[] { "One", "Two" }), "Procon display variables preserve groups, types and read-only flags");
        check(settings.Single(item => item.Name == "Info|Host").Value == session.Profile.Host && settings.Single(item => item.Name == "Info|Port").Value == session.Profile.Port.ToString(), "Procon OnPluginLoaded receives real instance endpoint instead of bridge endpoint");
        check(settings.Single(item => item.Name == "Info|Lines").Value.Contains("A%2bB", StringComparison.OrdinalIgnoreCase), "Procon string-array values retain upstream encoding");
        await session.Plugins.SetSettingAsync(settingsPath, "Messages|Message", "persisted");
        await session.Plugins.EnableAsync(settingsPath);
        await mock.EmitAsync("player.onSpawn", "ProconSpawn", "1");
        await Wait(() => mock.Received.Any(words => words.Contains("spawn ProconSpawn persisted")));
        check(true, "Procon event dispatch sees persisted grouped setting");
        var peerPath = Path.Combine(root, "PeerProbe.cs");
        var peer = File.ReadAllText(settingsPath).Replace("SettingsProbe", "PeerProbe")
            .Replace("public void OnPluginEnable() { }", "public void OnPluginEnable() { ExecuteCommand(\"procon.protected.plugins.call\", \"SettingsProbe\", \"Echo\", \"Procon peer\"); }");
        File.WriteAllText(peerPath, peer);
        await session.Plugins.EnableAsync(peerPath);
        await Wait(() => mock.Received.Any(words => words.Contains("peer Procon peer")));
        check(true, "two Procon plugins call each other in shared server runtime");
        check((await session.Plugins.GetSettingsAsync(settingsPath)).Single(item => item.Name == "Messages|Message").Value == "persisted", "Procon settings survive runtime rebuild when another plugin is added");
        var luaPath = Path.Combine(root, "Interop.lua");
        File.WriteAllText(luaPath, "event.plugin_enable:set(function() assert(procon.call('SettingsProbe', 'Echo', 'Lua peer') == 'echo Lua peer') end)");
        await session.Plugins.EnableAsync(luaPath);
        await Wait(() => mock.Received.Any(words => words.Contains("peer Lua peer")));
        check(true, "Lua can call enabled Procon plugin methods");
        await session.Plugins.DisableAsync(luaPath);
        await session.Plugins.DisableAsync(peerPath);
        await session.Plugins.DisableAsync(settingsPath);
        await session.Plugins.EnableAsync(settingsPath);
        check((await session.Plugins.GetSettingsAsync(settingsPath)).Single(item => item.Name == "Messages|Message").Value == "persisted", "Procon configuration survives disable and enable");
        await session.Plugins.DisableAsync(settingsPath);
        check(new ProfileStore(store.Root).State.Servers[0].PluginSettings[settingsPath]["Messages|Message"] == "persisted", "Procon settings saved outside compatibility working directory");
        var binaryDirectory = Directory.CreateDirectory(Path.Combine(root, "binary-dependency")).FullName;
        var binaryPath = Path.Combine(binaryDirectory, "BinaryProbe_API.cs");
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319");
        var helperBuild = CSharpCompilation.Create("BinaryProbe_API",
            [CSharpSyntaxTree.ParseText("public class BinaryHelper { public string Name() { return \"Binary helper loaded\"; } }")],
            [MetadataReference.CreateFromFile(Path.Combine(framework, "mscorlib.dll"))], new(OutputKind.DynamicallyLinkedLibrary));
        using (var helperOutput = File.Create(binaryPath))
        {
            var result = helperBuild.Emit(helperOutput);
            if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));
        }
        var binaryPluginPath = Path.Combine(binaryDirectory, "BinaryProbe.cs");
        File.WriteAllText(binaryPluginPath, File.ReadAllText(settingsPath).Replace("SettingsProbe", "BinaryProbe")
            .Replace("return \"Settings probe\";", "return helperName;")
            .Replace("private string message", """
                private readonly string helperName;
                public BinaryProbe() {
                    var helper = System.Reflection.Assembly.LoadFrom(@"Plugins\BF4\BinaryProbe_API.cs").CreateInstance("BinaryHelper");
                    helperName = (string)helper.GetType().GetMethod("Name").Invoke(helper, null);
                }
                private string message
                """));
        await session.Plugins.ImportAsync(binaryPluginPath);
        check((await session.Plugins.GetDetailsAsync(binaryPluginPath)).Name == "Binary helper loaded", "Procon stages managed assemblies named .cs and skips compiling them as source");
        var stagedBinary = Path.Combine(store.ServerDirectory(session.Profile.Id), "plugins", "procon", "Plugins", "BF4", "BinaryProbe_API.cs");
        check(File.ReadAllBytes(binaryPath).SequenceEqual(File.ReadAllBytes(stagedBinary)), "Procon binary .cs dependencies retain their original bytes");
        var packagePath = Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP")!;
        using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
        var upstream = Directory.CreateDirectory(Path.Combine(root, "upstream-plugins", "BF4")).FullName;
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) && !entry.FullName["Plugins/".Length..].Contains('/') && entry.Name.Length > 0))
            entry.ExtractToFile(Path.Combine(Path.GetDirectoryName(upstream)!, entry.Name), true);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("Plugins/BF4/", StringComparison.OrdinalIgnoreCase) && entry.Name.Length > 0 && !entry.FullName["Plugins/BF4/".Length..].Contains('/')))
            entry.ExtractToFile(Path.Combine(upstream, entry.Name), true);
        foreach (var name in new[] { "CSpambot", "CInGameAdmin" })
        {
            var upstreamPath = Path.Combine(upstream, name + ".cs");
            await session.Plugins.ImportAsync(upstreamPath);
            var upstreamDetails = await session.Plugins.GetDetailsAsync(upstreamPath);
            check(upstreamDetails.Name.Length > 0 && (await session.Plugins.GetSettingsAsync(upstreamPath)).Length > 0, name + " original Procon plugin compiles with includes and exposes metadata/settings");
        }
        await session.Plugins.EnableAsync(settingsPath);
    }
}
