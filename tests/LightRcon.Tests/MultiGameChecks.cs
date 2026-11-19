using System.Net;
using System.Net.Sockets;
using LightRcon.Core;

namespace LightRcon.Tests;

internal static class MultiGameChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        foreach (var game in Enum.GetValues<GameId>())
        {
            var maps = MapCatalog.For(game);
            check(SettingsCatalog.Supports("vars.hacker", game) == (game == GameId.BFHL) && SettingsCatalog.Supports("vars.commander", game) == (game == GameId.BF4) && SettingsCatalog.Supports("vars.ctfRoundTimeModifier", game) == (game == GameId.BF3), game + " has explicit game-specific setting ownership");
            check(maps.Count >= 27 && maps.All(x => x.Modes.Count > 0), game + " has a separate embedded map and mode catalog");
            var link = GameCatalog.PlayerProfile(game, "Test name/#&");
            var battlelogGame = game == GameId.BFHL ? "bfh" : game == GameId.BF3 ? "bf3" : "bf4";
            check(link.AbsoluteUri == $"https://battlelog.battlefield.com/{battlelogGame}/user/Test%20name%2F%23%26", game + " Battlelog URL uses the game and escapes the full player name");
            if (game == GameId.BF4) continue;
            await using var mock = new MockFrostbiteServer { GameCode = game.ToString(), Map = maps[0].FileName, GameMode = maps[0].Modes[0].Code, AdvertiseAllSettings = true };
            lock (mock.Maps) { mock.Maps.Clear(); mock.Maps.Add(new(mock.Map, mock.GameMode, 2)); }
            var store = new ProfileStore(Path.Combine(root, game.ToString()));
            var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
            // An old endpoint may be repointed to a different game; never upload its old draft.
            profile.DraftSettings["vars.serverName"] = "Stale BF4 draft";
            profile.DesiredSettings["vars.commander"] = "true";
            store.State.Servers.Add(profile);
            await using var session = new ServerSession(profile, store);
            await session.StartAsync(); await Wait(() => session.IsConnected);
            check(session.Game == game && profile.Game == game && session.Info.Game == game && !profile.DraftSettings.ContainsKey("vars.serverName"), game + " autodetection replaces stale game-specific drafts before reconciliation");
            check(session.Info.MapName == maps[0].Name && session.MapSnapshot()[0].Game == game && session.MapSnapshot()[0].Name == maps[0].Name, game + " live state and rotation use the detected game catalog");
            check(!session.IsSupported("vars.commander") && (game != GameId.BFHL || session.IsSupported("vars.hacker")), game + " settings follow advertised capabilities");
            check(!mock.Received.Any(words => SettingsCatalog.Find(words[0]) is not null && !SettingsCatalog.Supports(words[0], game)) && session.SettingsSnapshot().Keys.All(command => SettingsCatalog.Supports(command, game)), game + " ignores settings from other games even when advertised by the server");
            var countBefore = mock.Received.Count;
            try { await session.ApplySettingsAsync(new() { ["vars.commander"] = "false" }); throw new Exception("Cross-game setting accepted"); }
            catch (ArgumentException) { check(mock.Received.Count == countBefore && !profile.DesiredSettings.ContainsKey("vars.commander"), game + " rejects cross-game changes before persisting or sending"); }
            try { await session.ExecuteAsync(["vars.commander", "false"]); throw new Exception("Cross-game raw setting accepted"); }
            catch (ArgumentException) { check(!mock.Received.Any(words => words[0] == "vars.commander"), game + " raw commands cannot bypass setting ownership"); }
            profile.DraftSettings["vars.commander"] = "true";
            check(!SettingsCatalog.StartupConfig(profile).Contains("vars.commander"), game + " startup export omits an obsolete setting from another game");
            profile.DraftSettings.Remove("vars.commander");
            var luaPath = Path.Combine(store.Root, "GameScope.lua");
            File.WriteAllText(luaPath, game == GameId.BF3 ? "assert(vars.hacker == nil and vars.commander == nil and vars.preset == nil and vars.ctfRoundTimeModifier ~= nil); plugin = {name='Game scoped Lua'}" : "assert(vars.hacker ~= nil and vars.commander == nil and vars.gunMasterWeaponsPreset == nil and vars.roundStartReadyPlayersNeeded ~= nil); plugin = {name='Game scoped Lua'}");
            await session.Plugins.EnableAsync(luaPath);
            check(session.Plugins.Snapshot().Single().Enabled, game + " Lua namespaces omit variables from other games");
            if (game == GameId.BF3)
            {
                await session.ApplySettingsAsync(new() { ["vars.gunMasterWeaponsPreset"] = "8" });
                check(mock.Settings["vars.gunMasterWeaponsPreset"] == "8" && !session.IsSupported("vars.preset") && session.SpectatorSnapshot().Length == 0, "BF3 supports its nine Gun Master presets and tolerates missing spectator commands");
            }
            await session.ReplaceMapsAsync([new(maps[1].FileName, maps[1].Modes[0].Code, 3) { Game = game }]);
            await session.SwitchMapAsync(0, session.MapSnapshot());
            check(mock.LastSwitchedIndex == 0 && session.MapSnapshot()[0].Game == game, game + " rotation edits and immediate map switching retain game context");
            var player = "Example_Alpha";
            await mock.EmitAsync("player.onKill", "Example_Bravo", player, "U_M416", "false");
            await Wait(() => session.PlayerSnapshot().First(x => x.Name == player).IsAlive == false);
            await session.RefreshAsync(false);
            check(session.PlayerSnapshot().First(x => x.Name == player).IsAlive == false && session.Journal.Snapshot().Last(x => x.Kind == "Kill").Game == game, game + " kill events stay dead across polling and retain journal game context");
            await mock.EmitAsync("player.onSpawn", player, "1");
            await Wait(() => session.PlayerSnapshot().First(x => x.Name == player).IsAlive == true);
            check(true, game + " spawn event restores the alive state");
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            profile.Layer.Port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); profile.Layer.BindAddress = "127.0.0.1";
            await session.SaveLayerAccountAsync(false, "admin", "MultiGame-admin-test", true, LayerPermission.All); await session.StartLayerAsync();
            var downstream = new ServerProfile { Host = "127.0.0.1", Port = profile.Layer.Port, Username = "admin", ProtectedPassword = SecretProtection.Protect("MultiGame-admin-test") };
            await using var layer = new ServerSession(downstream, store); await layer.StartAsync(); await Wait(() => layer.IsConnected);
            check(layer.Game == game && layer.MapSnapshot().All(x => x.Game == game), game + " Layer connections autodetect their upstream game");
            check(layer.SettingsSnapshot().Keys.All(command => SettingsCatalog.Supports(command, game)) && !layer.IsSupported("vars.commander"), game + " Layer settings retain game separation");
            await mock.EmitAsync("player.onKill", "Example_Bravo", player, "U_M416", "false");
            await Wait(() => layer.PlayerSnapshot().First(x => x.Name == player).IsAlive == false);
            await mock.EmitAsync("player.spawn", player, "1");
            await Wait(() => layer.PlayerSnapshot().First(x => x.Name == player).IsAlive == true);
            check(true, game + " Layer forwards kill and historical spawn events");
            try { _ = GameCatalog.PlayerProfile(game, player, true); throw new Exception("BF4DB accepted another game"); }
            catch (ArgumentException) { check(true, game + " cannot generate misleading BF4DB links"); }
        }
        check(GameCatalog.PlayerProfile(GameId.BF4, "name &?", true).AbsoluteUri == "https://bf4db.com/player/search?query=name%20%26%3F", "BF4DB search safely escapes player names");
        var bootstrapStore = new ProfileStore(Path.Combine(root, "bootstrap"));
        Environment.SetEnvironmentVariable("LIGHTRCON_TEST_RCON", "Bootstrap-rcon-secret");
        Environment.SetEnvironmentVariable("LIGHTRCON_TEST_ADMIN", "Bootstrap-admin-secret");
        try
        {
            string[] args = ["--connect", "[::1]:47200", "--rcon-password-env", "LIGHTRCON_TEST_RCON", "--admin-user", "admin", "--admin-password-env", "LIGHTRCON_TEST_ADMIN", "--layer-port", "47400", "--configure-only"];
            var options = HostOptions.Parse(args); var profile = options.Bootstrap(bootstrapStore); options.Bootstrap(bootstrapStore);
            var restored = new ProfileStore(Path.Combine(root, "bootstrap")).State.Servers.Single();
            check(options.ConfigureOnly && restored.Host == "::1" && restored.AutoLogin && restored.Layer.Enabled && restored.Layer.Port == 47400 && restored.Layer.BindAddress == "127.0.0.1", "headless bootstrap saves a loopback Layer and RCON profile without connecting");
            check(restored.Layer.Accounts.Count == 1 && restored.Layer.Accounts[0].Permissions == LayerPermission.All && PasswordHash.Verify("Bootstrap-admin-secret", restored.Layer.Accounts[0].PasswordHash), "repeated bootstrap updates a single fully authorized hashed administrator");
            check(SecretProtection.Unprotect(restored.ProtectedPassword) == "Bootstrap-rcon-secret" && !File.ReadAllText(Path.Combine(root, "bootstrap", "profiles.json")).Contains("-secret"), "bootstrap stores protected connection secrets and never plaintext passwords");
            foreach (var invalid in new string[][] { ["--unknown"], ["--connect"], ["--connect", "localhost:47200"], ["--admin-user", "admin"], ["--configure-only"], [.. args, "--layer-port", "0"], [.. args, "--rcon-password", "conflict"] })
            {
                try { HostOptions.Parse(invalid); throw new Exception("Invalid host options accepted"); }
                catch (ArgumentException) { }
            }
            check(true, "headless parser rejects unknown, incomplete, conflicting and duplicate options before mutation");
        }
        finally { Environment.SetEnvironmentVariable("LIGHTRCON_TEST_RCON", null); Environment.SetEnvironmentVariable("LIGHTRCON_TEST_ADMIN", null); }
    }
    private static async Task Wait(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!predicate()) await Task.Delay(25, timeout.Token);
    }
}
