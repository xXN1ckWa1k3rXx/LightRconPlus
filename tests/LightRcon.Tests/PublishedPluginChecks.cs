using System.Diagnostics;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class PublishedPluginChecks
{
    public static async Task<int> Run(string host)
    {
        var root = Path.Combine(Path.GetTempPath(), "LightRcon-published-" + Guid.NewGuid().ToString("N"));
        using var process = new Process();
        var started = false;
        try
        {
            await using var mock = new MockFrostbiteServer();
            var store = new ProfileStore(root);
            var native = Path.Combine(root, "PublishedNative.cs");
            File.WriteAllText(native, """
                using LightRcon.Core.Plugins;
                [assembly: LightRconPlus_only(1)]
                public sealed class PublishedNative : ILightRconPlugin {
                    public void Enable(PluginContext context) => context.CommandAsync("admin.say", "published native", "all").GetAwaiter().GetResult();
                    public void OnEvent(string[] words) { }
                    public void Dispose() { }
                }
                """);
            var lua = Path.Combine(root, "PublishedLua.lua");
            File.WriteAllText(lua, "event.plugin_enable:set(function() admin.say('all', 'published lua') end)");
            var profile = new ServerProfile { Name = "Published smoke", Host = "127.0.0.1", Port = mock.Port, AutoLogin = true, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password), EnabledPlugins = [native, lua] };
            var expected = new List<string> { "published native", "published lua" };
            if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP") is { } archive)
            {
                File.Copy(archive, Path.Combine(root, "Procon-v1.5.4.2.zip"));
                var legacy = Path.Combine(root, "PublishedProcon.cs");
                File.WriteAllText(legacy, """
                    using System.Collections.Generic;
                    using PRoCon.Core;
                    using PRoCon.Core.Plugin;
                    namespace PRoConEvents {
                        public class PublishedProcon : PRoConPluginAPI, IPRoConPluginInterface {
                            public string GetPluginName() { return "Published Procon"; }
                            public string GetPluginVersion() { return "1.0"; }
                            public string GetPluginAuthor() { return "Test"; }
                            public string GetPluginWebsite() { return ""; }
                            public string GetPluginDescription() { return ""; }
                            public void OnPluginLoaded(string host, string port, string version) { }
                            public void OnPluginEnable() { ExecuteCommand("procon.protected.send", "admin.say", "published procon", "all"); }
                            public void OnPluginDisable() { }
                            public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable>(); }
                            public List<CPluginVariable> GetDisplayPluginVariables() { return GetPluginVariables(); }
                            public void SetPluginVariable(string name, string value) { }
                        }
                    }
                    """);
                profile.EnabledPlugins.Add(legacy); expected.Add("published procon");
            }
            store.State.Servers.Add(profile); store.Save();
            process.StartInfo = new ProcessStartInfo(Path.GetFullPath(host)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            process.StartInfo.ArgumentList.Add("--data-dir"); process.StartInfo.ArgumentList.Add(root);
            process.Start();
            started = true;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            while (!expected.All(message => mock.Received.Any(words => words.Contains(message))))
            {
                if (process.HasExited) throw new Exception("Published host exited: " + await output + await errors);
                await Task.Delay(50, timeout.Token);
            }
            Console.WriteLine("PASS: published single-file host loads native C#, Lua and enabled compatibility plugins");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (started && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
