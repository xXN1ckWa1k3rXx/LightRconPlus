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
                    public string AccountSummary(string soldierName) {
                        var privileges = GetAccountPrivileges(soldierName);
                        return privileges == null ? "none" : privileges.CanKillPlayers + "|" + privileges.CanPermanentlyBanPlayers + "|" + privileges.CanUseMapFunctions + "|" + privileges.CanAlterServerSettings;
                    }
                    public void RefreshPlayers() { ExecuteCommand("procon.protected.send", "serverInfo"); ExecuteCommand("procon.protected.send", "admin.listPlayers", "all"); }
                    public string LocalizedTeams() {
                        var keys = new[] { "global.conquest.us", "global.conquest.ru", "global.conquest.cn", "global.rush.attackers", "global.rush.defenders", "global.conquest.cop", "global.conquest.crim", "global.Squad4" };
                        var names = new List<string>();
                        foreach (var key in keys) names.Add(GetLocalized(key, key));
                        return string.Join("|", names.ToArray());
                    }
                    public string ResolveTeam(string map, string mode, string name) {
                        foreach (var definition in GetMapDefines())
                            if (definition.FileName == map && definition.PlayList == mode) { mode = definition.GameMode; break; }
                        var team = GetTeamNameByFormattedTeamName("{GameMode} {FileName} {TeamName}", mode + " " + map + " " + name);
                        return team == null ? "none" : team.TeamID.ToString();
                    }
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
        var localizationProbe = Path.Combine(root, "LocalizationProbe.lua");
        File.WriteAllText(localizationProbe, """
            event.plugin_enable:set(function()
                local names = procon.call('SettingsProbe', 'LocalizedTeams')
                assert(names == 'US Army|Russian Army|Chinese Army|Attackers|Defenders|Cops|Criminals|Delta', names)
                assert(procon.call('SettingsProbe', 'ResolveTeam', 'MP_Abandoned', 'ConquestLarge0', 'US Army') == '2', 'US team ID')
                assert(procon.call('SettingsProbe', 'ResolveTeam', 'MP_Damage', 'ConquestLarge0', 'Chinese Army') == '2', 'CN team ID')
                assert(procon.call('SettingsProbe', 'ResolveTeam', 'MP_Damage', 'RushLarge0', 'Attackers') == '1', 'Rush team ID')
                assert(procon.call('SettingsProbe', 'ResolveTeam', 'MP_Damage', 'SquadDeathMatch0', 'Delta') == '4', 'Squad team ID')
            end)
            """);
        await session.Plugins.EnableAsync(localizationProbe); await session.Plugins.DisableAsync(localizationProbe);
        check(true, "headless Procon plugins resolve faction, Rush, squad and Hardline names from the upstream English language file");
        check(true, "Procon formatted team lookup matches readable names to the correct map/mode team IDs");
        var permissionProbe = Path.Combine(root, "AccountProbe.lua");
        async Task AssertAccount(string expected, string label)
        {
            File.WriteAllText(permissionProbe, "event.plugin_enable:set(function() assert(procon.call('SettingsProbe', 'AccountSummary', 'Example_Alpha') == '" + expected + "') end)");
            await session.Plugins.EnableAsync(permissionProbe);
            await session.Plugins.DisableAsync(permissionProbe);
            check(true, label);
        }
        await session.SaveLayerAccountAsync(false, "Example_Alpha", "Admin-plugin-password", true, LayerPermission.View | LayerPermission.Players);
        await AssertAccount("True|False|False|False", "Procon GetAccountPrivileges matches a Layer username to the soldier and grants only its Players scope");
        await session.SaveLayerAccountAsync(false, "Example_Alpha", "", true, LayerPermission.View | LayerPermission.Maps | LayerPermission.Settings | LayerPermission.Bans);
        await AssertAccount("True|True|True|True", "Procon account permissions update immediately without reloading the plugin");
        await session.SaveLayerAccountAsync(false, "Example_Alpha", "", false, LayerPermission.All);
        await AssertAccount("none", "disabled Layer accounts have no Procon in-game administrator privileges");
        await session.SaveLayerAccountAsync(false, "Example_Alpha", "", true, null);
        await session.SetGlobalPermissionsAsync(false, LayerPermission.View | LayerPermission.Maps);
        await AssertAccount("False|False|True|False", "Procon accounts inherit current Layer global permissions");
        await session.DeleteLayerAccountAsync(false, "Example_Alpha");
        await AssertAccount("none", "deleted Layer accounts are removed from Procon administrator lookup");
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
        await session.SaveLayerAccountAsync(false, "Example_Alpha", "Admin-plugin-password", true, LayerPermission.View | LayerPermission.Players);
        var adminPath = Path.Combine(upstream, "CInGameAdmin.cs");
        await session.Plugins.EnableAsync(adminPath);
        var refreshPath = Path.Combine(root, "RefreshPlayers.lua");
        File.WriteAllText(refreshPath, "event.plugin_enable:set(function() procon.call('SettingsProbe', 'RefreshPlayers') end)");
        var playerReads = mock.Received.Count(words => words[0] == "admin.listPlayers");
        await session.Plugins.EnableAsync(refreshPath);
        await Wait(() => mock.Received.Count(words => words[0] == "admin.listPlayers") > playerReads);
        await Task.Delay(150);
        var kills = mock.Received.Count(words => words[0] == "admin.killPlayer");
        await mock.EmitAsync("player.onChat", "Example_Alpha", "!kill Example_Bravo", "all");
        await Wait(() => mock.Received.Count(words => words[0] == "admin.killPlayer" && words.ElementAtOrDefault(1) == "Example_Bravo") > kills);
        check(true, "original CInGameAdmin executes an in-game kill for the matching Layer account with Players permission");
        var originalMap = mock.Map; var originalMode = mock.GameMode;
        foreach (var (map, teamName) in new[] { ("MP_Abandoned", "US Army"), ("MP_Damage", "Chinese Army") })
        {
            mock.Map = map; mock.GameMode = "ConquestLarge0";
            await session.Plugins.DisableAsync(refreshPath); await session.Plugins.EnableAsync(refreshPath);
            await Task.Delay(150);
            var beforeNuke = mock.Received.Count;
            await mock.EmitAsync("player.onChat", "Example_Alpha", "!nuke " + teamName, "all");
            await Wait(() => mock.Received.Skip(beforeNuke).Any(words => words.Contains("Nuking " + teamName)));
            check(true, "original CInGameAdmin accepts !nuke " + teamName + " and uses the readable name in its response");
            await Wait(() => mock.Received.Skip(beforeNuke).Any(words => words.SequenceEqual(new[] { "admin.killPlayer", "Example_Bravo" })));
            check(mock.Received.Skip(beforeNuke).Where(words => words[0] == "admin.killPlayer").All(words => words[1] == "Example_Bravo")
                && !mock.Received.Skip(beforeNuke).Where(words => words[0] is "admin.say" or "admin.yell").Any(words => words[1].Contains("global.conquest.")),
                "original CInGameAdmin nuke targets only " + teamName + " players and its countdown contains no resource keys");
        }
        mock.Map = originalMap; mock.GameMode = originalMode;
        await session.Plugins.DisableAsync(refreshPath); await session.Plugins.EnableAsync(refreshPath);
        await session.SaveLayerAccountAsync(false, "Example_Alpha", "", true, LayerPermission.View);
        kills = mock.Received.Count(words => words[0] == "admin.killPlayer");
        var beforeDenied = mock.Received.Count;
        await mock.EmitAsync("player.onChat", "Example_Alpha", "!kill Example_Bravo", "all");
        await Wait(() => mock.Received.Skip(beforeDenied).Any(words => words[0] == "admin.say" && words.Any(word => word.Contains("not have enough privileges to kill"))));
        check(mock.Received.Count(words => words[0] == "admin.killPlayer") == kills, "original CInGameAdmin denies the next kill immediately after Layer Players permission is revoked");
        await session.Plugins.DisableAsync(adminPath); await session.Plugins.DisableAsync(refreshPath);
        await session.DeleteLayerAccountAsync(false, "Example_Alpha");
    }
}
