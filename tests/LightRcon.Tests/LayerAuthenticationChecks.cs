using System.Net;
using System.Net.Sockets;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class LayerAuthenticationChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
        const string password = "Existing-Layer-密码-$-12345";
        var data = Path.Combine(root, "layer-authentication-existing");
        var store = new ProfileStore(data);
        var profile = new ServerProfile { Name = "Same displayed name", Host = "127.0.0.1", Port = 47200 };
        profile.Layer.BindAddress = "127.0.0.1";
        profile.Layer.Port = FreePort();
        foreach (var name in new[] { "admin", "viewer", "disabled", "noview" })
            profile.Layer.Accounts.Add(new() { Username = name, PasswordHash = PasswordHash.Create(password), Enabled = name != "disabled", Permissions = name == "noview" ? LayerPermission.Chat : name == "admin" ? LayerPermission.All : LayerPermission.View });
        store.State.Servers.Add(profile); store.Save();
        var hashes = profile.Layer.Accounts.Select(account => account.PasswordHash).ToArray();
        var reloaded = new ProfileStore(data);
        var restored = reloaded.State.Servers.Single();
        await using var host = new ServerSession(restored, reloaded);
        await host.StartLayerAsync();
        ServerProfile Client(string username, string secret, int? port = null) => new() { Host = "127.0.0.1", Port = port ?? restored.Layer.Port, Username = username, ProtectedPassword = SecretProtection.Protect(secret) };
        foreach (var username in new[] { "admin", "viewer" })
        {
            await using var client = new LayerConnection();
            await client.ConnectAsync(Client(username, password), default);
        }
        check(restored.Id == profile.Id && restored.Layer.Accounts.Select(account => account.PasswordHash).SequenceEqual(hashes), "Reloading existing Layer data preserves profile identity and administrator/user password hashes");
        check(true, "Existing administrator and regular Layer users authenticate after a data-store reload");

        async Task Denied(string name, string secret, string reason)
        {
            await using var client = new LayerConnection();
            try { await client.ConnectAsync(Client(name, secret), default); throw new Exception("Unexpectedly accepted " + name); }
            catch (UnauthorizedAccessException error)
            {
                check(error.Message == UiText.Get("Invalid username, password or login permission.") && host.ConsoleSnapshot().Any(entry => entry.Kind == "Layer" && entry.Text.Contains(reason)), "Layer authentication diagnostics distinguish " + reason + " while preserving the client-facing rejection");
            }
        }
        await Denied("missing", password, "AccountNotFound");
        await Denied("disabled", password, "AccountDisabled");
        await Denied("admin", "Wrong-layer-password", "PasswordMismatch");
        await Denied("noview", password, "ViewPermissionMissing");
        check(host.ConsoleSnapshot().All(entry => !entry.Text.Contains(password) && !entry.Text.Contains("Wrong-layer-password") && !hashes.Any(hash => entry.Text.Contains(hash))), "Layer diagnostics do not expose plaintext passwords or password hashes");

        var otherStore = new ProfileStore(Path.Combine(root, "layer-authentication-other-directory"));
        var other = new ServerProfile { Name = profile.Name, Host = profile.Host, Port = profile.Port };
        other.Layer.BindAddress = "127.0.0.1"; other.Layer.Port = FreePort();
        otherStore.State.Servers.Add(other); otherStore.Save();
        await using var otherHost = new ServerSession(other, otherStore);
        await otherHost.StartLayerAsync();
        await using var otherClient = new LayerConnection();
        try { await otherClient.ConnectAsync(Client("viewer", password, other.Layer.Port), default); throw new Exception("Account leaked between data directories"); }
        catch (UnauthorizedAccessException)
        {
            check(other.Id != profile.Id && otherHost.ConsoleSnapshot().Any(entry => entry.Text.Contains("AccountNotFound")), "Identically named endpoints in separate data directories have independent Layer accounts and profile IDs");
        }
    }
}
