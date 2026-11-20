using LightRcon.Core;
using LightRcon.Core.Plugins;

namespace LightRcon.Tests;

internal static class IncludeAndViewChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var library = Path.Combine(root, "include-library");
        var folder = Directory.CreateDirectory(Path.Combine(library, "BF4")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(library, "Shared", "Nested")).Parent!.FullName;
        var included = Path.Combine(shared, "Nested", "Label.inc");
        File.WriteAllText(included, "private string Label() { return \"父目录 include\"; }");
        File.WriteAllText(Path.Combine(shared, "BF4.inc"), "#include\t\"Nested/Label.inc\" // nested relative include");
        var source = Path.Combine(folder, "IncludeProbe.cs");
        var marker = Path.Combine(root, "include-constructions.txt");
        File.WriteAllText(source, $$"""
            using System.IO;
            using System.Collections.Generic;
            using LightRcon.Core.Plugins;
            [assembly: LightRconPlus_only(1)]
            public class IncludeProbe : ILightRconPlugin, IPluginConfiguration {
                private int reads;
                public IncludeProbe() { File.AppendAllText(@"{{marker}}", "created;"); }
                #include "../Shared/%GameType%.inc"
                public PluginDetails Details => new("", Label(), "1", "", "", "", "C#");
                public IReadOnlyList<PluginSetting> GetSettings() => new[] { new PluginSetting("Reads", "int", (++reads).ToString(), true) };
                public void SetSetting(string name, string value) { }
                public void Enable(PluginContext context) { }
                public void OnEvent(string[] words) { }
                public void Dispose() { File.AppendAllText(@"{{marker}}", "disposed;"); }
            }
            """);
        var copyLibrary = Path.Combine(root, "copied-include-library");
        var copied = await PluginLibrary.CopyAsync(source, GameId.BF4, copyLibrary);
        // The copied package must work after its original parent includes disappear.
        File.Move(included, included + ".saved");
        await using var mock = new MockFrostbiteServer();
        var store = new ProfileStore(Path.Combine(root, "include-store"));
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        await using var session = new ServerSession(profile, store, pluginDirectory: copyLibrary);
        await session.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!session.IsConnected || session.Plugins.IsRestoring || !File.Exists(marker)) await Task.Delay(25, timeout.Token);
        var first = await session.Plugins.GetViewAsync(copied);
        check(first.Details.Name == "父目录 include" && first.Details.Error.Length == 0, "Native C# expands parent, nested and game-specific includes from a portable copied package");
        var cached = session.Plugins.GetViewAsync(copied);
        check(cached.IsCompletedSuccessfully && (await cached).Settings.SequenceEqual(first.Settings) && File.ReadAllText(marker) == "created;", "Selecting initialized plugins uses cached settings without invoking getters or constructing runtimes");
        var refreshed = await session.Plugins.GetViewAsync(copied, refresh: true);
        check(int.Parse(refreshed.Settings.Single().Value) > int.Parse(first.Settings.Single().Value), "Explicit settings reload refreshes the cached plugin view");
        await session.Plugins.EnableAsync(copied);
        await session.Plugins.DisableAsync(copied);
        var before = File.ReadAllText(marker);
        await session.Plugins.GetViewAsync(copied);
        await session.Plugins.GetDetailsAsync(copied);
        await session.Plugins.GetSettingsAsync(copied);
        check(File.ReadAllText(marker) == before, "Viewing a disabled native plugin preserves its cached view without recreating its runtime");
        File.Move(included + ".saved", included);
        await session.Plugins.ImportAsync(source);
        check((await session.Plugins.GetViewAsync(source)).Details.Name == "父目录 include", "Referenced native C# plugins resolve ../ includes directly");
        var cyclic = Path.Combine(folder, "Cyclic.cs");
        File.WriteAllText(cyclic, "using LightRcon.Core.Plugins;\n[assembly: LightRconPlus_only(1)]\n#include \"Cycle.inc\"");
        File.WriteAllText(Path.Combine(folder, "Cycle.inc"), "#include \"Cyclic.cs\"");
        try { await session.Plugins.ImportAsync(cyclic); throw new Exception("Cyclic include was accepted"); }
        catch (InvalidDataException error) { check(error.Message.Contains("Cyclic"), "Cyclic includes fail with an explicit diagnostic"); }
    }
}
