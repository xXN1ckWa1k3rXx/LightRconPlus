using System.IO.Compression;
using System.Security.Cryptography;
using LightRcon.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LightRcon.Tests;

internal static class ProconCacheChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP") is not { } archivePath) return;
        var library = Path.Combine(root, "procon-cache-library");
        var folder = Directory.CreateDirectory(Path.Combine(library, "BF4")).FullName;
        var store = new ProfileStore(Path.Combine(root, "procon-cache-store"));
        File.Copy(archivePath, Path.Combine(store.Root, "Procon-v1.5.4.2.zip"));
        using (var archive = ZipFile.OpenRead(archivePath)) archive.GetEntry("PRoCon.Core.dll")!.ExtractToFile(Path.Combine(folder, "PRoCon.Core.dll"));
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319");
        var references = new[] { "mscorlib.dll", "System.dll", "System.Core.dll" }.Select(name => MetadataReference.CreateFromFile(Path.Combine(framework, name)))
            .Append(MetadataReference.CreateFromFile(Path.Combine(folder, "PRoCon.Core.dll"))).ToArray();
        const string template = """
            using System.Collections.Generic;
            using PRoCon.Core;
            using PRoCon.Core.Plugin;
            namespace PRoConEvents {
                public partial class CacheProbe : PRoConPluginAPI, IPRoConPluginInterface {
                    public string GetPluginName() { return "Cache probe"; }
                    public string GetPluginVersion() { return "1"; }
                    public string GetPluginAuthor() { return "Test"; }
                    public string GetPluginWebsite() { return ""; }
                    public string GetPluginDescription() { return "Cache fixture"; }
                    public void OnPluginLoaded(string host, string port, string version) { }
                    public void OnPluginEnable() { }
                    public void OnPluginDisable() { }
                    public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable>(); }
                    public List<CPluginVariable> GetDisplayPluginVariables() { return GetPluginVariables(); }
                    public void SetPluginVariable(string name, string value) { }
                }
            }
            """;
        void Binary(string name, string version)
        {
            var code = template.Replace("CacheProbe", name).Replace("return \"1\";", "return \"" + version + "\";");
            var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(code)], references, new(OutputKind.DynamicallyLinkedLibrary));
            using var output = File.Create(Path.Combine(folder, name + ".dll"));
            var result = compilation.Emit(output);
            if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));
        }
        var source = Path.Combine(folder, "CacheProbe.cs");
        File.WriteAllText(source, template.Replace("public string GetPluginVersion() { return \"1\"; }", "#include \"../Shared/%GameType%.inc\""));
        var shared = Directory.CreateDirectory(Path.Combine(library, "Shared", "Nested")).Parent!.FullName;
        File.WriteAllText(Path.Combine(shared, "BF4.inc"), "#include\t\"Nested/CacheProbe.inc\" // nested relative include");
        var include = Path.Combine(shared, "Nested", "CacheProbe.inc");
        File.WriteAllText(include, "public string GetPluginVersion() { return Extra(); }");
        var partial = Path.Combine(folder, "CacheProbe.More.cs");
        File.WriteAllText(partial, "namespace PRoConEvents { public partial class CacheProbe { private string Extra() { return \"1\"; } } }");
        var supplied = Path.Combine(folder, "SuppliedProbe.cs");
        // An include-only source with a missing include must still load its existing DLL.
        File.WriteAllText(supplied, "#include \"../UnavailableSource.inc\"");
        Binary("SuppliedProbe", "binary-v1");
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        await using var session = new ServerSession(profile, store, pluginDirectory: library);
        async Task Ready()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!session.IsConnected || session.Plugins.IsRestoring || session.Plugins.Snapshot().Length != 2 || session.Plugins.Snapshot().Any(item => item.Kind.Length == 0)) await Task.Delay(25, timeout.Token);
            var failed = session.Plugins.Snapshot().Where(item => item.Error.Length > 0).ToArray();
            if (failed.Length > 0) throw new Exception(string.Join(Environment.NewLine, failed.Select(item => item.Path + ": " + item.Error)));
        }
        bool UsedCache(string name, int from) => session.ConsoleSnapshot().Skip(from).Any(entry => entry.Text.Contains("Compiling " + name + ".cs...") && entry.Text.Contains("Using Cache"));
        bool Compiled(string name, int from) => session.ConsoleSnapshot().Skip(from).Any(entry => entry.Text.Contains("Compiling " + name + ".cs...") && entry.Text.Contains("Done"));
        await session.StartAsync(); await Ready();
        var staged = Path.Combine(store.ServerDirectory(profile.Id), "plugins", "procon", "Plugins", "BF4");
        var cachedDll = Path.Combine(staged, "CacheProbe.dll");
        var binaryHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "SuppliedProbe.dll")));
        check(UsedCache("SuppliedProbe", 0) && session.Plugins.Snapshot().Single(item => item.Path == supplied).Version == "binary-v1"
            && binaryHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(staged, "SuppliedProbe.dll")))), "Procon loads an adjacent compiled DLL byte-for-byte without compiling or requiring its original includes");
        check(Compiled("CacheProbe", 0) && File.Exists(cachedDll), "Procon compiles source when no matching DLL is available");
        var cachedTime = File.GetLastWriteTimeUtc(cachedDll);
        var cachedHash = SHA256.HashData(File.ReadAllBytes(cachedDll));
        var from = session.ConsoleSnapshot().Length;
        await session.StopAsync(); await session.StartAsync(); await Ready();
        check(UsedCache("CacheProbe", from) && UsedCache("SuppliedProbe", from) && File.GetLastWriteTimeUtc(cachedDll) == cachedTime && cachedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(cachedDll))), "Procon keeps source cache entries across reconnect and reuses unchanged DLLs without rewriting them");
        from = session.ConsoleSnapshot().Length;
        File.WriteAllText(Path.Combine(folder, "PluginCache.xml"), "<PluginCache><Entries /></PluginCache>");
        File.WriteAllText(Path.Combine(folder, "NewLua.lua"), "plugin={name='Cache invalidation neighbor'}");
        await session.Plugins.RefreshAsync();
        check(UsedCache("CacheProbe", from) && !Compiled("CacheProbe", from), "Adding another plugin reuses unchanged Procon DLLs without replacing the runtime cache with a library PluginCache.xml");
        from = session.ConsoleSnapshot().Length;
        File.WriteAllText(partial, File.ReadAllText(partial).Replace("return \"1\"", "return \"2\""));
        await session.Plugins.RefreshAsync();
        check(Compiled("CacheProbe", from) && session.Plugins.Snapshot().Single(item => item.Path == source).Version == "2" && UsedCache("SuppliedProbe", from), "Changing a partial source recompiles only that plugin and leaves other cached DLLs intact");
        from = session.ConsoleSnapshot().Length;
        File.WriteAllText(include, "public string GetPluginVersion() { return Extra() + \"-更新\"; }");
        await session.Plugins.RefreshAsync();
        if (!Compiled("CacheProbe", from) || session.Plugins.Snapshot().Single(item => item.Path == source).Version != "2-更新")
        {
            Console.Error.WriteLine("Include refresh metadata: " + System.Text.Json.JsonSerializer.Serialize(session.Plugins.Snapshot().Single(item => item.Path == source)));
            foreach (var entry in session.ConsoleSnapshot().Skip(from)) Console.Error.WriteLine(entry.Formatted);
        }
        check(Compiled("CacheProbe", from) && session.Plugins.Snapshot().Single(item => item.Path == source).Version == "2-更新", "Changing a Unicode include invalidates the matching Procon source cache");
        from = session.ConsoleSnapshot().Length;
        Binary("SuppliedProbe", "binary-v2");
        await session.Plugins.RefreshAsync();
        check(UsedCache("SuppliedProbe", from) && session.Plugins.Snapshot().Single(item => item.Path == supplied).Version == "binary-v2" && UsedCache("CacheProbe", from), "Replacing an adjacent compiled DLL loads the new binary directly and preserves unrelated source caches");
        from = session.ConsoleSnapshot().Length;
        File.WriteAllText(supplied, template.Replace("CacheProbe", "SuppliedProbe").Replace("return \"1\";", "return \"source-v3\";"));
        await session.Plugins.RefreshAsync();
        check(Compiled("SuppliedProbe", from) && session.Plugins.Snapshot().Single(item => item.Path == supplied).Version == "source-v3", "Updating source after loading a supplied DLL compiles the changed source instead of reusing an obsolete binary");
        from = session.ConsoleSnapshot().Length;
        await session.StopAsync(); await session.StartAsync();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            while (!session.IsConnected || session.Plugins.IsRestoring || session.Plugins.Snapshot().Length < 3) await Task.Delay(25, timeout.Token);
        check(UsedCache("SuppliedProbe", from) && session.Plugins.Snapshot().Single(item => item.Path == supplied).Version == "source-v3", "Reconnect keeps the newly compiled DLL rather than overwriting it with the library's older binary");
        from = session.ConsoleSnapshot().Length;
        await session.StopAsync();
        File.Delete(cachedDll);
        await session.StartAsync();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            while (!session.IsConnected || session.Plugins.IsRestoring || session.Plugins.Snapshot().Length < 3) await Task.Delay(25, timeout.Token);
        check(Compiled("CacheProbe", from) && session.Plugins.Snapshot().Single(item => item.Path == source).Version == "2-更新", "A missing cached DLL is recompiled even when source hashes are unchanged");
    }
}
