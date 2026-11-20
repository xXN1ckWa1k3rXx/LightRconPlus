using LightRcon.Core;
using LightRcon.Core.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO.Compression;

namespace LightRcon.Tests;

internal static class PluginDiscoveryChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> predicate, int seconds = 20)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            while (!predicate()) await Task.Delay(25, timeout.Token);
        }
        var library = Path.Combine(root, "auto-library");
        var folder = Directory.CreateDirectory(Path.Combine(library, "BF4")).FullName;
        var otherGame = Directory.CreateDirectory(Path.Combine(library, "BF3")).FullName;
        File.WriteAllText(Path.Combine(otherGame, "WrongGame.lua"), "plugin={name='Wrong game'}");
        var nativePath = Path.Combine(folder, "AutoNative.cs");
        File.WriteAllText(nativePath, """
            using LightRcon.Core.Plugins;
            [assembly: LightRconPlus_only(1)]
            public class AutoNative : ILightRconPlugin {
                public void Enable(PluginContext context) { context.Log("auto native enabled"); }
                public void OnEvent(string[] words) { }
                public void Dispose() { }
            }
            """);
        File.WriteAllText(Path.ChangeExtension(nativePath, ".dll"), "old cache must not override source");
        File.WriteAllText(Path.Combine(folder, "MySql.Data.dll"), "invalid dependency must not become a plugin");
        var luaPath = Path.Combine(folder, "AutoLua.lua");
        File.WriteAllText(luaPath, "plugin={name='Automatic Lua v1'}; settings.define('Value', 'string', 'default')");
        var store = new ProfileStore(Path.Combine(root, "auto-library-store"));
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Name = "Discovery host", Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);

        string? proconPath = null;
        if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP") is { } archivePath)
        {
            File.Copy(archivePath, Path.Combine(store.Root, "Procon-v1.5.4.2.zip"));
            using var archive = ZipFile.OpenRead(archivePath);
            archive.GetEntry("PRoCon.Core.dll")!.ExtractToFile(Path.Combine(folder, "PRoCon.Core.dll"));
            var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319");
            var references = new[] { "mscorlib.dll", "System.dll", "System.Core.dll" }.Select(name => MetadataReference.CreateFromFile(Path.Combine(framework, name)))
                .Append(MetadataReference.CreateFromFile(Path.Combine(folder, "PRoCon.Core.dll"))).ToArray();
            void Compile(string path, string code)
            {
                var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path), [CSharpSyntaxTree.ParseText(code)], references, new(OutputKind.DynamicallyLinkedLibrary));
                using var output = File.Create(path);
                var result = compilation.Emit(output);
                if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));
            }
            Compile(Path.Combine(folder, "BF4DB_API.cs"), "public class Dependency { }");
            Compile(Path.Combine(folder, "MySql.Data.dll"), "public class Dependency { }");
            var template = """
                using System;
                using System.IO;
                using System.Collections.Generic;
                using PRoCon.Core;
                using PRoCon.Core.Plugin;
                namespace PRoConEvents {
                    public class AutoLegacy : PRoConPluginAPI, IPRoConPluginInterface {
                        public AutoLegacy() {
                            System.Reflection.Assembly.LoadFrom(@"Plugins\BF4\BF4DB_API.cs");
                            if (File.ReadAllText(@"Plugins\BF4\proconrulz_rules.txt") != "rules fixture") throw new Exception("Missing rules");
                        }
                        public string GetPluginName() { return "AutoLegacy"; }
                        public string GetPluginVersion() { return "1"; }
                        public string GetPluginAuthor() { return "Test"; }
                        public string GetPluginWebsite() { return ""; }
                        public string GetPluginDescription() { return "Library discovery fixture"; }
                        public void OnPluginLoaded(string host, string port, string version) { }
                        public void OnPluginEnable() { }
                        public void OnPluginDisable() { }
                        public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable>(); }
                        public List<CPluginVariable> GetDisplayPluginVariables() { return GetPluginVariables(); }
                        public void SetPluginVariable(string name, string value) { }
                    }
                }
                """;
            File.WriteAllText(Path.Combine(folder, "proconrulz_rules.txt"), "rules fixture");
            proconPath = Path.Combine(folder, "AutoLegacy.cs");
            File.WriteAllText(proconPath, template);
            Compile(Path.ChangeExtension(proconPath, ".dll"), template);
            Compile(Path.Combine(folder, "BinaryLegacy.dll"), template.Replace("AutoLegacy", "BinaryLegacy"));
            var discovered = PluginLibrary.Discover(library, GameId.BF4);
            check(discovered.Contains(proconPath) && discovered.Contains(Path.Combine(folder, "BinaryLegacy.dll")) && !discovered.Contains(Path.ChangeExtension(proconPath, ".dll")), "Flat library discovery prefers Procon source over cached DLLs and accepts standalone plugin DLLs");
            check(!discovered.Any(path => Path.GetFileName(path) is "BF4DB_API.cs" or "MySql.Data.dll" or "PRoCon.Core.dll"), "Flat library discovery excludes binary .cs helpers, database dependencies and PRoCon.Core.dll");
        }

        await using var host = new ServerSession(profile, store, pluginDirectory: library);
        await host.StartAsync();
        await Wait(() => host.IsConnected && !host.Plugins.IsRestoring && host.Plugins.Snapshot().Any(item => item.Path == nativePath && item.Kind == "C#"));
        foreach (var failed in host.Plugins.Snapshot().Where(item => item.Error.Length > 0)) Console.Error.WriteLine(failed.Path + ": " + failed.Error);
        check(host.Plugins.Snapshot().All(item => item.Error.Length == 0 && !item.Enabled) && host.Plugins.Snapshot().Any(item => item.Name == "Automatic Lua v1") && !host.Plugins.Snapshot().Any(item => item.Name == "Wrong game"), "Connecting discovers and compiles game-specific library plugins while leaving new plugins disabled");
        if (proconPath is not null)
        {
            check(host.Plugins.Snapshot().Count(item => item.Kind == "Procon") == 2, "Core startup registers a full Procon batch before starting the shared compiler");
            check(host.ConsoleSnapshot().Count(item => item.Text.Contains("Compiling AutoLegacy.cs")) == 1, "Startup compiles each discovered Procon source once rather than rebuilding per plugin");
            var staged = Path.Combine(store.ServerDirectory(profile.Id), "plugins", "procon", "Plugins", "BF4");
            check(File.ReadAllText(Path.Combine(staged, "proconrulz_rules.txt")) == "rules fixture" && File.ReadAllBytes(Path.Combine(staged, "BF4DB_API.cs")).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "BF4DB_API.cs"))), "Auto-discovered Procon constructors receive text resources and unmodified binary .cs dependencies");
        }
        await host.Plugins.SetSettingAsync(luaPath, "Value", "saved");
        await host.Plugins.EnableAsync(luaPath);
        var addedPath = Path.Combine(folder, "AddedLater.lua");
        File.WriteAllText(addedPath, "plugin={name='Added after startup'}");
        await Task.Delay(TimeSpan.FromSeconds(6));
        check(!host.Plugins.Snapshot().Any(item => item.Path == addedPath), "Running Core does not periodically scan new plugin files");

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); profile.Layer.Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        profile.Layer.Accounts.Add(new() { Username = "viewer", PasswordHash = PasswordHash.Create("Library-viewer-password"), Permissions = LayerPermission.View, Enabled = true });
        await host.StartLayerAsync();
        var clientStore = new ProfileStore(Path.Combine(root, "auto-library-client"));
        var clientProfile = new ServerProfile { Host = "127.0.0.1", Port = profile.Layer.Port, Username = "viewer", ProtectedPassword = SecretProtection.Protect("Library-viewer-password") };
        clientStore.State.Servers.Add(clientProfile);
        await using var client = new ServerSession(clientProfile, clientStore);
        await client.StartAsync();
        await Wait(() => client.IsConnected && !client.Plugins.IsRestoring && client.Plugins.Snapshot().Any(item => item.Path == addedPath));
        await Task.Delay(300); // Allow the sign-in's initial plugin.list request to finish its unchanged scan.
        check(host.Plugins.Snapshot().Any(item => item.Path == addedPath) && client.Plugins.Snapshot().Single(item => item.Path == addedPath).Enabled == false, "Successful Layer login discovers new host library plugins and pushes the updated list");
        check((await host.Plugins.GetSettingsAsync(luaPath)).Single().Value == "saved" && host.Plugins.Snapshot().Single(item => item.Path == luaPath).Enabled, "Library changes preserve existing plugin settings and enabled state");

        File.WriteAllText(luaPath, "plugin={name='Automatic Lua v2'}; settings.define('Value', 'string', 'default')");
        var refreshedPath = Path.Combine(folder, "ManualRefresh.lua");
        File.WriteAllText(refreshedPath, "plugin={name='Manual refresh'}");
        var brokenPath = Path.Combine(folder, "BrokenLibrary.lua");
        File.WriteAllText(brokenPath, "invalid lua syntax )");
        var incompletePath = Path.Combine(folder, "IncompleteProcon.cs");
        if (proconPath is not null) File.WriteAllText(incompletePath, "#include \"../MissingPlugin.inc\"");
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (host.Plugins.Snapshot().Single(item => item.Path == luaPath).Name != "Automatic Lua v1" || host.Plugins.Snapshot().Any(item => item.Path == refreshedPath))
        {
            Console.Error.WriteLine($"Unexpected library change: client={client.State}; restoring={client.Plugins.IsRestoring}; error={client.LastError}; name={host.Plugins.Snapshot().Single(item => item.Path == luaPath).Name}");
            foreach (var entry in host.ConsoleSnapshot().Where(item => item.Kind is "Plugin" or "Layer")) Console.Error.WriteLine(entry.Formatted);
        }
        check(host.Plugins.Snapshot().Single(item => item.Path == luaPath).Name == "Automatic Lua v1" && !host.Plugins.Snapshot().Any(item => item.Path == refreshedPath), "Existing Layer connections do not cause background rescans of changed files");
        await client.Plugins.RefreshAsync();
        await Wait(() => client.Plugins.Snapshot().Any(item => item.Path == refreshedPath) && client.Plugins.Snapshot().Single(item => item.Path == luaPath).Name == "Automatic Lua v2");
        check((await host.Plugins.GetSettingsAsync(luaPath)).Single().Value == "saved" && client.Plugins.Snapshot().Single(item => item.Path == luaPath).Enabled, "Layer Refresh list reloads updated files, discovers additions and preserves configuration");
        check(client.Plugins.Snapshot().Single(item => item.Path == brokenPath).Error.Length > 0 && host.IsConnected, "A discovered plugin load failure reaches Layer clients without stopping healthy plugins or RCON");
        if (proconPath is not null)
            check(client.Plugins.Snapshot().Single(item => item.Path == incompletePath).Error.Contains("MissingPlugin.inc") && host.Plugins.Snapshot().Single(item => item.Path == proconPath).Error.Length == 0, "An incomplete include-only Procon package reports its missing file without preventing other plugins from compiling");
        var failures = host.ConsoleSnapshot().Count(item => item.Kind == "Plugin" && item.Text.StartsWith("BrokenLibrary.lua:"));
        var initialScans = host.ConsoleSnapshot().Count(item => item.Text.StartsWith("Scanning plugin library:"));
        await client.StopAsync(); await client.StartAsync();
        await Wait(() => client.IsConnected);
        await Task.Delay(200);
        check(host.ConsoleSnapshot().Count(item => item.Text.StartsWith("Scanning plugin library:")) == initialScans, "Layer reconnect with unchanged library avoids recompiling loaded plugins");
        check(host.ConsoleSnapshot().Count(item => item.Kind == "Plugin" && item.Text.StartsWith("BrokenLibrary.lua:")) == failures, "Unchanged failed library entries are not repeatedly loaded during Layer sign-in");
    }
}
