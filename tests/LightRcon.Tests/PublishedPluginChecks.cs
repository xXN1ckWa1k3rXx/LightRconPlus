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
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        try
        {
            await using var mock = new MockFrostbiteServer();
            var store = new ProfileStore(root);
            var native = Path.Combine(root, "PublishedNative.cs");
            File.WriteAllText(native, """
                using LightRcon.Core.Plugins;
                [assembly: LightRconPlus_only(1)]
                public sealed class PublishedNative : ILightRconPlugin {
                    public void Enable(PluginContext context) { context.Log("native plugin console marker"); context.CommandAsync("admin.say", "published native", "all").GetAwaiter().GetResult(); }
                    public void OnEvent(string[] words) { }
                    public void Dispose() { }
                }
                """);
            var lua = Path.Combine(root, "PublishedLua.lua");
            File.WriteAllText(lua, "event.plugin_enable:set(function() print('Lua print console marker'); log.info('Lua info console marker'); admin.say('all', 'published lua') end)");
            var profile = new ServerProfile { Name = "Published smoke", Host = "127.0.0.1", Port = mock.Port, AutoLogin = true, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password), EnabledPlugins = [native, lua] };
            var expected = new List<string> { "published native", "published lua" };
            var expectedOutput = new List<string> { "native plugin console marker", "Lua print console marker", "Lua info console marker" };
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
                            public void OnPluginEnable() { ExecuteCommand("procon.protected.pluginconsole.write", "Procon console marker"); ExecuteCommand("procon.protected.send", "admin.say", "published procon", "all"); }
                            public void OnPluginDisable() { }
                            public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable>(); }
                            public List<CPluginVariable> GetDisplayPluginVariables() { return GetPluginVariables(); }
                            public void SetPluginVariable(string name, string value) { }
                        }
                    }
                    """);
                profile.EnabledPlugins.Add(legacy); expected.Add("published procon");
                expectedOutput.Add("Procon console marker");
            }
            store.State.Servers.Add(profile); store.Save();
            process.StartInfo = new ProcessStartInfo(Path.GetFullPath(host)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            process.StartInfo.ArgumentList.Add("--data-dir"); process.StartInfo.ArgumentList.Add(root);
            process.OutputDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
            process.ErrorDataReceived += (_, args) => { if (args.Data is { } line) errors.Enqueue(line); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            started = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            while (!expected.All(message => mock.Received.Any(words => words.Contains(message))) || !expectedOutput.All(marker => output.Any(line => line.Contains(marker))))
            {
                if (process.HasExited) throw new Exception("Published host exited: " + string.Join("\n", output.Concat(errors)));
                await Task.Delay(50, timeout.Token);
            }
            Console.WriteLine("PASS: published single-file host loads native C#, Lua and enabled compatibility plugins");
            Console.WriteLine("PASS: Host stdout prints native C#, Lua print/log.info and Procon plugin console output");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            foreach (var line in output.Concat(errors)) Console.Error.WriteLine(line);
            return 1;
        }
        finally
        {
            if (started && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
