using System.Net;
using System.Net.Sockets;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class LayerPluginChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!predicate()) await Task.Delay(25, timeout.Token);
        }
        async Task Denied(Func<Task> action, string label)
        {
            try { await action(); }
            catch (RconException) { check(true, label); return; }
            throw new Exception("Unexpectedly accepted: " + label);
        }
        await using var mock = new MockFrostbiteServer();
        var store = new ProfileStore(Path.Combine(root, "layer-plugins-host"));
        var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
        store.State.Servers.Add(profile);
        await using var host = new ServerSession(profile, store);
        await host.StartAsync(); await Wait(() => host.IsConnected);
        var path = Path.Combine(root, "LayerHosted.lua");
        File.WriteAllText(path, """
            plugin = { name = 'Hosted plugin', author = 'Main Layer', version = '2' }
            settings.define('Message|Text', 'string', 'host value')
            settings.define('Message|Count', 'int', 1)
            settings.define('Info|Fixed', 'string', 'fixed', true)
            event.plugin_enable:set(function() admin.say('all', 'Layer plugin started') end)
            """);
        await host.Plugins.EnableAsync(path);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        profile.Layer.Port = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop(); profile.Layer.BindAddress = "127.0.0.1";
        await host.SaveLayerAccountAsync(false, "viewer", "Viewer-plugin-password", true, LayerPermission.View);
        await host.SaveLayerAccountAsync(false, "editor", "Editor-plugin-password", true, LayerPermission.View | LayerPermission.Settings);
        await host.SaveLayerAccountAsync(false, "admin", "Admin-plugin-password", true, LayerPermission.All);
        await host.StartLayerAsync();
        ServerProfile Client(string user) => new() { Host = "127.0.0.1", Port = profile.Layer.Port, Username = user, ProtectedPassword = SecretProtection.Protect(char.ToUpperInvariant(user[0]) + user[1..] + "-plugin-password") };
        var clientStore = new ProfileStore(Path.Combine(root, "layer-plugins-client"));
        var clientProfile = Client("viewer");
        var clientOnlyPath = Path.Combine(root, "MissingClientOnly.lua");
        clientProfile.InstalledPlugins.Add(clientOnlyPath); clientProfile.EnabledPlugins.Add(clientOnlyPath);
        clientStore.State.Servers.Add(clientProfile);
        await using var viewer = new ServerSession(clientProfile, clientStore);
        var settingQueries = mock.Received.Count(words => words.Length == 1 && SettingsCatalog.Find(words[0]) is not null);
        await viewer.StartAsync(); await Wait(() => viewer.IsConnected && viewer.Plugins.Snapshot().Length == 1);
        check(mock.Received.Count(words => words.Length == 1 && SettingsCatalog.Find(words[0]) is not null) == settingQueries, "Layer login reads the synchronized host setting snapshot without repeating upstream setting queries");
        check(viewer.Plugins.Snapshot().Single().Path == path && viewer.Plugins.Snapshot().Single().Enabled, "Layer lists only upstream plugins and ignores saved client-local plugins");
        check((await viewer.Plugins.GetDetailsAsync(path)).Author == "Main Layer" && (await viewer.Plugins.GetSettingsAsync(path)).Length == 3, "Layer viewer reads upstream plugin metadata and settings");
        await Wait(() => !viewer.Plugins.IsRestoring);
        check(viewer.Plugins.GetViewAsync(path).IsCompletedSuccessfully, "Layer plugin initialization caches views for immediate selection");
        await Denied(() => viewer.Plugins.SetSettingAsync(path, "Message|Text", "denied"), "Layer viewer cannot edit upstream plugin settings");
        await Denied(() => viewer.Plugins.DisableAsync(path), "Layer viewer cannot disable upstream plugins");
        await Denied(() => viewer.Plugins.GetDetailsAsync(clientOnlyPath), "Layer cannot load arbitrary host paths through metadata requests");
        await Denied(() => viewer.Plugins.EnableAsync(clientOnlyPath), "Layer cannot register a client path on its host");
        try { await viewer.Plugins.ImportAsync(path); throw new Exception("Client import accepted"); }
        catch (InvalidOperationException) { check(true, "Layer client imports are rejected without touching local profiles"); }
        await using var editor = new LayerConnection(); await editor.ConnectAsync(Client("editor"), default);
        await editor.RequestAsync(new() { Type = "plugin.set", Words = [path, "Message|Text", "remote edit"] });
        check((await host.Plugins.GetSettingsAsync(path)).Single(item => item.Name == "Message|Text").Value == "remote edit" && new ProfileStore(store.Root).State.Servers.Single().PluginSettings[path]["Message|Text"] == "remote edit", "Layer settings permission edits and persists the main host plugin configuration");
        await Wait(() => viewer.Plugins.Snapshot().Single().SettingsRevision == host.Plugins.Snapshot().Single().SettingsRevision);
        check((await viewer.Plugins.GetViewAsync(path)).Settings.Single(item => item.Name == "Message|Text").Value == "remote edit", "Plugin changes from another Layer client invalidate the cached settings view");
        await Denied(() => editor.RequestAsync(new() { Type = "plugin.set", Words = [path, "Message|Count", "bad"] }), "Layer rejects invalid typed plugin settings");
        await Denied(() => editor.RequestAsync(new() { Type = "plugin.set", Words = [path, "Info|Fixed", "bad"] }), "Layer rejects edits of read-only plugin settings");
        await Denied(() => editor.RequestAsync(new() { Type = "plugin.enable", Words = [path] }), "Settings permission alone cannot enable plugins");
        await Denied(() => editor.RequestAsync(new() { Type = "plugin.set", Words = [path] }), "Malformed remote plugin requests return errors without disconnecting");
        var adminProfile = Client("admin"); clientStore.State.Servers.Add(adminProfile);
        await using var admin = new ServerSession(adminProfile, clientStore);
        await admin.StartAsync(); await Wait(() => admin.IsConnected && admin.Plugins.Snapshot().Length == 1);
        check(mock.Received.Count(words => words.SequenceEqual(new[] { "admin.say", "Layer plugin started", "all" })) == 1, "Connecting Layer clients never launches duplicate plugin runtimes");
        await admin.Plugins.DisableAsync(path); await Wait(() => !viewer.Plugins.Snapshot().Single().Enabled);
        check(!host.Plugins.Snapshot().Single().Enabled, "Remote disable updates the host and pushes state to other Layer clients");
        await admin.Plugins.EnableAsync(path); await Wait(() => viewer.Plugins.Snapshot().Single().Enabled);
        check((await viewer.Plugins.GetSettingsAsync(path)).Single(item => item.Name == "Message|Text").Value == "remote edit", "Remote enable restores saved host settings");
        var added = Path.Combine(root, "AddedOnHost.lua"); File.WriteAllText(added, "plugin = { name = 'Added on host' }");
        await host.Plugins.ImportAsync(added); await Wait(() => viewer.Plugins.Snapshot().Length == 2 && admin.Plugins.Snapshot().Length == 2);
        check(true, "Main Layer plugin imports appear in all connected client lists");
        await viewer.StopAsync();
        check(host.Plugins.Snapshot().Single(item => item.Path == path).Enabled && viewer.Plugins.Snapshot().Length == 0, "Disconnecting Layer clears its remote cache and leaves host plugins running");
        await viewer.StartAsync(); await Wait(() => viewer.IsConnected && viewer.Plugins.Snapshot().Length == 2);
        check(viewer.Plugins.Enabled.SequenceEqual(new[] { path }) && clientProfile.PluginSettings.Count == 0, "Layer reconnect restores upstream state without persisting a client plugin configuration");
    }
}
