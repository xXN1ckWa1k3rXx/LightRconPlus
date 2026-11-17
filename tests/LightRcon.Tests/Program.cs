using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LightRcon.Core;
using LightRcon.Tests;

if (args.Contains("--mock"))
{
    await using var mock = new MockFrostbiteServer(); var dataIndex = Array.IndexOf(args, "--seed");
    if (dataIndex >= 0)
    {
        var seed = new ProfileStore(args[dataIndex + 1]); seed.State.Servers.Clear();
        seed.State.Servers.Add(new() { Name = "BF4 · Local protocol test", Host = "127.0.0.1", Port = mock.Port, AutoLogin = true, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) }); seed.Save();
    }
    Console.WriteLine($"Mock BF4 at 127.0.0.1:{mock.Port}. Test-only password: {MockFrostbiteServer.Password}");
    var exit = new TaskCompletionSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.TrySetResult(); }; await exit.Task; return 0;
}
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL: expected " + typeof(T).Name + " for " + name);
}
async Task Wait(Func<bool> predicate, string name, int seconds = 12)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    while (!predicate()) { try { await Task.Delay(25, timeout.Token); } catch (OperationCanceledException) { throw new Exception("Timeout: " + name); } } Check(true, name);
}
int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
var root = Path.Combine(Path.GetTempPath(), "LightRcon-tests-" + Guid.NewGuid().ToString("N"));
try
{
    await MultiGameChecks.Run(root, Check);
    var golden = new FrostbitePacket(0, false, false, ["version"]).Encode();
    Check(Convert.ToHexString(golden) == "0000000018000000010000000700000076657273696F6E00", "golden BF4 command packet uses zero origin bit");
    var unicode = new FrostbitePacket(11, true, false, ["player.onChat", "玩家", "hello 世界", "all"]); var parsed = await FrostbitePacket.ReadAsync(new MemoryStream(unicode.Encode()), default);
    Check(parsed.Sequence == 11 && parsed.OriginServer && parsed.Words.SequenceEqual(unicode.Words), "UTF-8 word lengths and event flags");
    var malformed = golden.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(4), int.MaxValue);
    await Reject<InvalidDataException>(async () => { await FrostbitePacket.ReadAsync(new MemoryStream(malformed), default); }, "reject oversized packet before allocation");
    Check(ServerProfile.ParseEndpoint("[::1]:47200") == ("::1", 47200), "IPv6 endpoint parser");
    await Reject<ArgumentException>(() => Task.Run(() => ServerProfile.ParseEndpoint("127.0.0.1")), "reject missing port");
    Check(CommandText.Parse("admin.say \"hello 世界\" all").SequenceEqual(new[] { "admin.say", "hello 世界", "all" }), "quoted console command");
    Check(CommandText.Parse("vars.serverDescription \"\"")[1] == "", "empty quoted argument");
    using (var badFrame = new MemoryStream())
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"Type\":\"command\",\"Words\":null}");
        var size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length); badFrame.Write(size); badFrame.Write(bytes); badFrame.Position = 0;
        await Reject<InvalidDataException>(async () => { await LayerWire.ReadAsync(badFrame, default); }, "reject null command array from malicious Layer peer");
    }
    Check(MapCatalog.All.Count >= 30 && MapCatalog.MapName("MP_Abandoned") == "Zavod 311", "BF4 map catalog including expansions");
    Check(new[] { "ConquestLarge0", "ConquestSmall0", "RushLarge0", "TeamDeathMatch0", "Domination0", "SquadDeathMatch0" }.Select(mode => TicketEstimate.Calculate(150, TicketEstimate.DefaultBase(mode)!.Value)).SequenceEqual(new decimal[] { 1200, 600, 150, 150, 450, 75 }), "150 percent ticket previews match all six requested mode defaults");
    Check(TicketEstimate.Calculate(150, 75) == 112.5m && TicketEstimate.DefaultBase("GunMaster0") is null, "ticket preview keeps fractional estimates and does not guess unknown mode defaults");
    string[] ServerWords(string[] scores) => ["OK", "BF4", "3", "64", "ConquestLarge0", "MP_Abandoned", "0", "2", scores.Length.ToString(), .. scores, "800", "InGame", "true", "true", "false", "93784", "150"];
    var decimalScores = FrostbiteParser.Server(ServerWords(["798.000000", "477.900000"]));
    Check(decimalScores.TeamScores.SequenceEqual(new int?[] { 798, 478 }) && decimalScores.TargetScore == 800 && decimalScores.Uptime == 93784, "decimal BF4 tickets are rounded like Procon without mixing in target score or uptime");
    var fourScores = FrostbiteParser.Server(ServerWords(["100.0000", "99.0000", "98.0000", "97.0000"]));
    Check(fourScores.TeamScores.SequenceEqual(new int?[] { 100, 99, 98, 97 }) && fourScores.Uptime == 93784, "four-team decimal scores preserve all team indexes and uptime offset");
    var invalidScore = FrostbiteParser.Server(ServerWords(["not-a-score", "0.0000"]));
    Check(invalidScore.TeamScores[0] is null && invalidScore.TeamScores[1] == 0, "unknown tickets stay unknown while real zero tickets remain zero");
    await Reject<InvalidDataException>(() => Task.Run(() => FrostbiteParser.Server(["OK", "BF4", "3", "64", "ConquestLarge0", "MP_Abandoned", "0", "2", "4", "700", "650", "800"])), "reject truncated team score blocks instead of reading unrelated fields");
    Check(TeamCatalog.Name("MP_Abandoned", "ConquestLarge0", 1) == "Russian Army" && TeamCatalog.Name("MP_Abandoned", "ConquestLarge0", 2) == "US Army" && TeamCatalog.Name("MP_Damage", "ConquestLarge0", 2) == "Chinese Army", "team factions follow Procon map definitions rather than a fixed ID mapping");
    Check(TeamCatalog.Name("XP5_Night_01", "ConquestLarge0", 1) == "Russian Army" && TeamCatalog.Name("XP5_Night_01", "TeamDeathMatch0", 1) == "US Army", "faction lookup accounts for different modes on the same map");
    Check(TeamCatalog.Name("MP_Abandoned", "RushLarge0", 1) == "Attackers (US Army)" && TeamCatalog.Name("MP_Abandoned", "SquadDeathMatch0", 4) == "Squad 4" && TeamCatalog.Name("UNKNOWN", "UNKNOWN", 3) == "Team 3", "Rush roles, four-squad modes and unknown-team fallback stay distinct");
    var rose = new ActivityEntry(1, DateTimeOffset.Now, "Chat", "U_AEK971", "all", "ID_CHAT_REQUEST_MEDIC");
    var kill = new ActivityEntry(2, DateTimeOffset.Now, "Kill", "ID_CHAT_THANKS", "", "ID_CHAT_THANKS killed Victim with U_AEK971") { TargetName = "Victim", Weapon = "U_AEK971" };
    foreach (var language in new[] { "en", "zh", "de" })
    {
        UiText.SetLanguage(language);
        var commoIds = UiText.Translations.Keys.Where(x => x.StartsWith("ID_CHAT_", StringComparison.Ordinal)).ToArray();
        Check(commoIds.Length == 13 && commoIds.All(x => GameText.Chat(x) != x), language + " resolves all 13 confirmed Commo Rose messages");
        var attackDefend = language == "zh" ? "[快捷指令] 进攻／防守！" : language == "de" ? "[Commo Rose] Angreifen / verteidigen!" : "[Commo Rose] Attack / defend!";
        Check(GameText.Chat("ID_CHAT_ATTACK/DEFEND") == attackDefend && GameText.Chat("  id_chat_attack/defend  ") == attackDefend, language + " resolves the exact BF4 slash-form attack/defend ID");
        Check(rose.DisplayText == GameText.Chat("id_chat_request_medic") && rose.Formatted.Contains("[U_AEK971]") && rose.Text == "ID_CHAT_REQUEST_MEDIC", language + " translates chat display while retaining player names and raw message IDs");
        Check(kill.DisplayText.Contains("AEK-971") && !kill.DisplayText.Contains("U_AEK971") && kill.DisplayText.StartsWith("ID_CHAT_THANKS ", StringComparison.Ordinal) && kill.Weapon == "U_AEK971", language + " translates only the weapon in structured kill events");
        Check(GameText.Weapon("u_aek971") == "AEK-971" && GameText.Weapon("U_ACR") == "ACW-R" && GameText.Weapon("U_M200") == "SRR-61" && GameText.Weapon("U_UGS") == "T-UGS", language + " handles case-insensitive weapon IDs and aliases rather than simply removing U_");
        Check(GameText.Weapon("U_AEK971_M320_HE").Contains("AEK-971") && GameText.Weapon("U_AEK971_M320_HE").Contains("M320 HE") && GameText.Weapon("U_AEK971_M320_HE").Contains(language == "zh" ? "挂载于" : language == "de" ? "Unterlauf" : "underbarrel"), language + " preserves underbarrel ammunition and its parent weapon");
    }
    UiText.SetLanguage("en");
    Check(GameText.Chat("  id_chat_not_yet_known  ") == "  id_chat_not_yet_known  " && GameText.Chat("id_chat_unknown/unknown") == "id_chat_unknown/unknown" && GameText.Chat("I typed ID_CHAT_REQUEST_MEDIC and U_AEK971") == "I typed ID_CHAT_REQUEST_MEDIC and U_AEK971" && GameText.Weapon("U_NOT_YET_KNOWN") == "U_NOT_YET_KNOWN", "unknown IDs and ordinary chat text are preserved verbatim");
    var legacyKill = new ActivityEntry(3, DateTimeOffset.Now, "Kill", "Pilot with spaces", "", "Pilot with spaces killed Victim with spaces with U_M416");
    Check(legacyKill.TryGetKillDetails(out var legacyTarget, out var legacyWeapon) && legacyTarget == "Victim with spaces" && legacyWeapon == "U_M416" && legacyKill.DisplayText.EndsWith("with M416", StringComparison.Ordinal), "legacy kill history resolves weapons without changing names containing with");
    var serializedKill = JsonSerializer.Serialize(kill);
    Check(!serializedKill.Contains("DisplayText") && !serializedKill.Contains("Formatted") && JsonSerializer.Deserialize<ActivityEntry>(serializedKill)!.Weapon == "U_AEK971", "journal and Layer serialization retain raw fields and omit localized presentation");
    await Reject<ArgumentException>(() => Task.Run(() => SettingsCatalog.Validate("vars.gunMasterWeaponsPreset", "99")), "reject unknown Gun Master preset");
    await Reject<ArgumentException>(() => Task.Run(() => SettingsCatalog.Validate("vars.preset", "CLASSIC maybe")), "reject malformed preset lock flag");
    await Reject<ArgumentException>(() => Task.Run(() => SettingsCatalog.Validate("vars.unlockMode", "arbitrary")), "reject unknown unlock choice");
    var secret = SecretProtection.Protect("test-secret"); Check(SecretProtection.Unprotect(secret) == "test-secret" && !secret.Contains("test-secret"), "Windows credential encryption");
    var hash = PasswordHash.Create("Test-password-1234"); Check(PasswordHash.Verify("Test-password-1234", hash) && !PasswordHash.Verify("wrong-password", hash), "salted Layer password hashing");
    var historyRoot = Path.Combine(root, "history-test"); Directory.CreateDirectory(historyRoot);
    File.WriteAllLines(Path.Combine(historyRoot, "activity-2026-11-17.jsonl"), Enumerable.Range(1, 5200).Select(i => JsonSerializer.Serialize(new ActivityEntry(i, DateTimeOffset.Now, "Chat", "Example", "all", "entry " + i))));
    var largeJournal = new ActivityJournal(historyRoot, true);
    Check(largeJournal.Snapshot().Length == 5000 && largeJournal.Replay().Sum(x => x.Length) == 5200, "history replay exceeds the bounded 5000-entry UI cache");
    using (var replay = largeJournal.Replay().GetEnumerator())
    {
        Check(replay.MoveNext(), "history replay begins streaming");
        largeJournal.Add("Chat", "Admin", "all", "append while replaying");
        while (replay.MoveNext()) { }
        Check(largeJournal.Replay().Sum(x => x.Length) == 5201, "live journal appends coexist with open replay readers");
    }
    var reopenedJournal = new ActivityJournal(historyRoot, false);
    Check(reopenedJournal.Snapshot().Length == 0, "disabled Layer never restores prior chat into direct session");
    reopenedJournal.Add("Chat", "Admin", "all", "new direct session"); reopenedJournal.SetPersistent(true);
    Check(reopenedJournal.Snapshot()[0].Sequence > 5201, "history IDs stay monotonic across disabling and re-enabling Layer");
    reopenedJournal.Add("Chat", "Admin", "all", "ID_CHAT_ATTACK/DEFEND");
    var rawReplay = new ActivityJournal(historyRoot, true).Replay().SelectMany(x => x).Last();
    UiText.SetLanguage("zh");
    Check(rawReplay.Text == "ID_CHAT_ATTACK/DEFEND" && rawReplay.DisplayText.Contains("进攻／防守"), "persisted raw slash-form chat IDs resolve in the current language when replayed");
    UiText.SetLanguage("en");
    Check(LayerAuthorization.Required(["vars.serverName"]) == LayerPermission.View && LayerAuthorization.Required(["vars.serverName", "x"]) == LayerPermission.Settings, "separate read and write permissions");
    await Reject<UnauthorizedAccessException>(() => Task.Run(() => LayerAuthorization.Required(["admin.password"])), "Layer cannot read RCON password");
    await Reject<UnauthorizedAccessException>(() => Task.Run(() => LayerAuthorization.Required(["punkBuster.pb_sv_command", "pb_sv_plist; pb_sv_exec evil.cfg"])), "deny PB command chaining");
    await using var mock = new MockFrostbiteServer();
    await using (var bad = new RconConnection()) await Reject<RconException>(() => bad.ConnectAsync("127.0.0.1", mock.Port, "wrong", default), "hashed login rejects invalid credentials");
    var store = new ProfileStore(root); var profile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, Name = "Test", AutoLogin = true, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) };
    store.State.Servers.Add(profile); store.Save(); await using var session = new ServerSession(profile, store); await session.StartAsync();
    await Wait(() => session.IsConnected, "session authenticates and loads server state");
    Check(session.PlayerSnapshot().Length == 3 && session.PlayerSnapshot()[0].Kdr == "3.00", "dynamic player fields and KDR");
    Check(session.Info.Uptime == 93784 && session.Info.Round == 1, "variable team score block and uptime offsets");
    Check(session.Info.TeamScores.SequenceEqual(new int?[] { 700, 650 }), "live session parses decimal team scores from the mock wire response");
    Check(session.BanSnapshot().Length == 2 && session.BanSnapshot()[1].Identifier == "Example_Banned", "ban list six fields without count prefix");
    Check(session.ReservedSnapshot().SequenceEqual(new[] { "Example_Reserved" }) && session.SpectatorSnapshot().Length == 1, "reserved and spectator pagination");
    await Wait(() => mock.EventAcknowledgments >= 2 && session.Journal.Snapshot().Any(x => x.Text.Contains("hello 世界")), "server events acknowledged and recorded");
    await Wait(() => session.Journal.Snapshot().Any(x => x.Text == "ID_CHAT_REQUEST_MEDIC") && session.Journal.Snapshot().Any(x => x.Text == "ID_CHAT_ATTACK/DEFEND") && session.Journal.Snapshot().Any(x => x.Weapon == "U_AEK971"), "mock wire chat and kill events retain their raw game identifiers including the slash-form ID");
    Check(session.Journal.Snapshot().First(x => x.Text == "ID_CHAT_REQUEST_MEDIC").DisplayText.Contains("Need a medic") && session.Journal.Snapshot().First(x => x.Text == "ID_CHAT_ATTACK/DEFEND").DisplayText.Contains("Attack / defend!") && session.Journal.Snapshot().First(x => x.Weapon == "U_AEK971").DisplayText.Contains("AEK-971"), "live event display resolves both chat and weapon identifiers including the slash-form ID");
    Check(!Directory.EnumerateFiles(store.ServerDirectory(profile.Id), "activity-*.jsonl").Any(), "direct-only chat never written to disk");
    await session.ApplySettingsAsync(new() { ["vars.serverName"] = "Local preferred" }); Check(mock.Settings["vars.serverName"] == "Local preferred", "apply local setting");
    await session.ApplySettingsAsync(new() { ["vars.preset"] = "NORMAL" });
    Check(mock.Settings["vars.friendlyFire"] == "true", "preset changes do not overwrite locally preferred individual settings");
    Check(mock.Received.Any(x => x.SequenceEqual(new[] { "vars.preset", "NORMAL", "false" })), "legacy preset names migrate to the full name and lock protocol arguments");
    await session.ApplySettingsAsync(new() { ["vars.preset"] = "CLASSIC true", ["vars.gunMasterWeaponsPreset"] = "3", ["vars.unlockMode"] = "common" });
    Check(mock.PresetLocked && mock.Settings["vars.preset"] == "CLASSIC" && mock.Settings["vars.gunMasterWeaponsPreset"] == "3" && mock.Settings["vars.unlockMode"] == "common", "preset lock, Gun Master ID and unlock code reach the server");
    var presetConfig = SettingsCatalog.StartupConfig(profile);
    Check(CommandText.Parse(presetConfig.Split('\n').First(x => x.StartsWith("vars.preset "))).SequenceEqual(new[] { "vars.preset", "CLASSIC", "true" }), "startup config exports preset name and lock as separate arguments");
    await session.ReplaceMapsAsync([new("MP_Damage", "ConquestLarge0", 3)]); Check(mock.Maps.Count == 1 && mock.Maps[0].Rounds == 3, "map mutation and save");
    Check(mock.DiskMaps.SequenceEqual(mock.Maps), "map edits persist the same rotation to the server MapList.txt");
    await Reject<RconException>(() => session.ReplaceMapsAsync([new("INVALID_MAP", "ConquestLarge0", 2)]), "server rejects unavailable map");
    Check(mock.Maps.Count == 1 && mock.Maps[0].FileName == "MP_Damage", "failed map replacement restores original rotation");
    Check(profile.DesiredMaps!.SequenceEqual(mock.Maps), "rejected map is removed from the local preferred rotation");
    await Task.WhenAll(
        session.MutateMapsAsync(maps => { maps.Add(new("MP_Abandoned", "ConquestLarge0", 7)); return maps; }),
        session.MutateMapsAsync(maps => { maps.Add(new("MP_Journey", "RushLarge0", 4)); return maps; }));
    Check(mock.Maps.Count == 3 && mock.Maps[1].Rounds == 7 && mock.Maps[2].Rounds == 4 && profile.DesiredMaps!.SequenceEqual(mock.Maps), "rapid additions retain both maps and their round counts after rejection");
    await session.ReplaceMapsAsync([new("MP_Damage", "ConquestLarge0", 3)]);
    await session.ExecuteAsync(["mapList.add", "MP_Abandoned", "ConquestLarge0", "2"]);
    Check(profile.DesiredMaps?.Count == 2, "console map mutations update local preferred rotation");
    lock (mock.Maps) mock.Maps.Add(new("MP_Journey", "RushLarge0", 4));
    await session.RefreshAsync();
    Check(profile.DesiredMaps!.SequenceEqual(mock.Maps) && profile.DesiredMaps?.Count == 3 && mock.DiskMaps.Count == 1, "polling follows external live rotation changes without silently overwriting the disk file");
    lock (mock.Maps)
    {
        mock.DiskMaps.Clear();
        mock.DiskMaps.AddRange([new("MP_Abandoned", "ConquestLarge0", 2), new("MP_Abandoned", "RushLarge0", 3), new("MP_Abandoned", "ConquestLarge0", 2)]);
    }
    var savesBeforeLoad = mock.Received.Count(x => x[0] == "mapList.save");
    await session.LoadMapsFromDiskAsync();
    Check(session.MapSnapshot().SequenceEqual(mock.DiskMaps) && profile.DesiredMaps!.SequenceEqual(mock.DiskMaps) && mock.Maps.SequenceEqual(mock.DiskMaps), "loading MapList.txt replaces both the live and locally preferred rotations");
    Check(mock.Received.Count(x => x[0] == "mapList.save") == savesBeforeLoad, "loading the file does not immediately rewrite its contents");
    mock.RejectMapLoad = true;
    await Reject<RconException>(() => session.LoadMapsFromDiskAsync(), "file load reports server errors");
    Check(mock.Maps.SequenceEqual(mock.DiskMaps) && session.MapSnapshot().SequenceEqual(mock.DiskMaps) && mock.Received.Count(x => x[0] == "mapList.save") == savesBeforeLoad, "failed load restores the live list without overwriting the source MapList.txt");
    mock.RejectMapLoad = false;
    await session.SwitchMapAsync(2, session.MapSnapshot());
    var switchCommands = mock.Received.Where(x => x[0] is "mapList.setNextMapIndex" or "mapList.runNextRound").TakeLast(2).ToArray();
    Check(mock.LastSwitchedIndex == 2 && switchCommands[0].SequenceEqual(new[] { "mapList.setNextMapIndex", "2" }) && switchCommands[1].SequenceEqual(new[] { "mapList.runNextRound" }), "immediate switch uses the exact duplicate row index and sets it before running the next round");
    var runs = mock.Received.Count(x => x[0] == "mapList.runNextRound");
    await session.SetNextMapAsync(1, session.MapSnapshot());
    Check(mock.NextMapIndex == 1 && mock.Received.Count(x => x[0] == "mapList.runNextRound") == runs, "setting the next map alone never switches immediately");
    mock.RejectNextMap = true;
    await Reject<RconException>(() => session.SwitchMapAsync(0, session.MapSnapshot()), "immediate switch reports a rejected next-map selection");
    Check(mock.Received.Count(x => x[0] == "mapList.runNextRound") == runs, "a rejected next-map index never runs another map");
    mock.RejectNextMap = false;
    var staleMaps = session.MapSnapshot();
    lock (mock.Maps) mock.Maps.RemoveAt(0);
    await Reject<ArgumentException>(() => session.SwitchMapAsync(1, staleMaps), "stale map indexes are rejected before switching");
    Check(mock.Received.Count(x => x[0] == "mapList.runNextRound") == runs && profile.DesiredMaps!.SequenceEqual(mock.Maps), "stale selection refreshes the displayed rotation without changing the current map");
    lock (mock.Maps) mock.Maps.Add(new("MP_Damage", "ConquestLarge0", 3));
    await session.RefreshMapsAsync();
    Check(profile.DesiredMaps!.SequenceEqual(mock.Maps) && session.MapSnapshot().SequenceEqual(mock.Maps), "manual refresh adopts the remote list instead of continuing to display an old local copy");
    mock.Settings["vars.serverName"] = "Another administrator changed this";
    await session.ApplySettingsAsync(new() { ["vars.serverName"] = "Local preferred" });
    Check(mock.Settings["vars.serverName"] == "Local preferred", "setting apply compares fresh remote state");
    await Wait(() => session.PlayerSnapshot()[0].PbGuid.Length == 32, "PunkBuster GUID and IP enrichment");
    profile.Layer.Port = FreePort(); profile.Layer.BindAddress = "127.0.0.1";
    await session.SaveLayerAccountAsync(false, "viewer", "Viewer-test-password", true, LayerPermission.View);
    await session.SaveLayerAccountAsync(false, "admin", "Admin-test-password", true, LayerPermission.All); await session.StartLayerAsync();
    Check(session.LayerFingerprint.Length == 64 && Directory.EnumerateFiles(store.ServerDirectory(profile.Id), "activity-*.jsonl").Any(), "Layer persists chat without connected clients");
    var replayed = session.Journal.Replay().SelectMany(x => x).ToArray();
    Check(replayed.Any(x => x.Text.Contains("hello 世界")), "full persisted history replay includes pre-Layer session messages");
    var lp = new ServerProfile { Host = "127.0.0.1", Port = profile.Layer.Port, Username = "viewer", ProtectedPassword = SecretProtection.Protect("Viewer-test-password") };
    await using (var accountOnly = new LayerConnection())
    {
        await accountOnly.ConnectAsync(lp, default);
        Check((await accountOnly.ExecuteAsync(["serverInfo"]))[0] == "OK", "Layer login uses username and password without a certificate pin");
    }
    var wrongLayer = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(lp))!;
    wrongLayer.ProtectedPassword = SecretProtection.Protect("wrong-layer-password");
    await using (var rejectedLayer = new LayerConnection())
        await Reject<UnauthorizedAccessException>(() => rejectedLayer.ConnectAsync(wrongLayer, default), "Layer login rejects incorrect account password");
    await using var viewer = new LayerConnection(); List<ActivityEntry> history = []; viewer.HistoryReceived += entries => { lock (history) history.AddRange(entries); }; await viewer.ConnectAsync(lp, default);
    await Wait(() => { lock (history) return history.Any(x => x.Text.Contains("hello 世界")); }, "new Layer receives historical chat");
    await Wait(() => { lock (history) return history.Any(x => x.Text == "ID_CHAT_REQUEST_MEDIC") && history.Any(x => x.Text == "ID_CHAT_ATTACK/DEFEND" && x.DisplayText.Contains("Attack / defend!")) && history.Any(x => x.Weapon == "U_AEK971"); }, "Layer replay carries original chat and weapon IDs for client-side localization including the slash-form ID");
    Check((await viewer.ExecuteAsync(["serverInfo"]))[0] == "OK", "Layer viewer reads state");
    await Reject<RconException>(() => viewer.ExecuteAsync(["mapList.load"]), "Layer viewer cannot load a file over the live rotation");
    await Reject<RconException>(() => viewer.ExecuteAsync(["mapList.setNextMapIndex", "0"]), "Layer viewer cannot select the next map");
    await Reject<RconException>(() => viewer.ExecuteAsync(["mapList.runNextRound"]), "Layer viewer cannot switch maps immediately");
    await Reject<RconException>(() => viewer.ExecuteAsync(["admin.kickPlayer", "Example_Alpha"]), "viewer cannot kick");
    await Reject<RconException>(() => viewer.ExecuteAsync(["admin.password"]), "password cannot be retrieved through Layer");
    await Reject<RconException>(() => viewer.RequestAsync(new() { Type = "layer.get" }), "viewer cannot inspect accounts");
    await Reject<RconException>(() => viewer.RequestAsync(new() { Type = "console", Words = ["serverInfo"] }), "console permission enforced server-side");
    var before = history.Count; await session.ExecuteAsync(["admin.say", "new persistent message", "all"]);
    await Wait(() => { lock (history) return history.Count > before && history.Any(x => x.Text == "new persistent message"); }, "live chat stored before forwarding");
    var configProfile = new ServerProfile { Username = "viewer", ProtectedPassword = profile.ProtectedPassword };
    Check(SettingsCatalog.StartupConfig(configProfile).Contains("*MUST REPLACE TO YOUR RCON PASSWORD*") && !SettingsCatalog.StartupConfig(configProfile).Contains(MockFrostbiteServer.Password), "Layer startup config redacts credentials");
    configProfile.DesiredMaps = [new("MP_Abandoned", "ConquestLarge0", 2)];
    configProfile.DraftSettings["vars.serverName"] = "Pending local name";
    var startup = SettingsCatalog.StartupConfig(configProfile);
    Check(startup.Contains("Pending local name") && !startup.Contains("Map rotation") && !startup.Contains("MP_Abandoned") && !startup.Contains("mapList."), "startup config includes current drafts and excludes map rotation");
    var ap = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(lp))!; ap.Username = "admin"; ap.ProtectedPassword = SecretProtection.Protect("Admin-test-password");
    await using var admin = new LayerConnection(); await admin.ConnectAsync(ap, default); var options = await admin.RequestAsync(new() { Type = "layer.get" });
    Check(options.Layer?.Accounts.Length == 2 && !JsonSerializer.Serialize(options).Contains("PasswordHash", StringComparison.OrdinalIgnoreCase), "account management never transmits password hashes");
    await admin.RequestAsync(new() { Type = "layer.account.save", Username = "newadmin", Password = "New-admin-password", Layer = new(true, "", 0, 0, [new("newadmin", true, LayerPermission.View)]) });
    Check(profile.Layer.Accounts.Any(x => x.Username == "newadmin"), "remote account mutation acknowledged before invalidation");
    var badProfile = new ServerProfile { Host = "127.0.0.1", Port = mock.Port, ProtectedPassword = SecretProtection.Protect("wrong"), AutoLogin = true }; store.State.Servers.Add(badProfile);
    await using var failing = new ServerSession(badProfile, store); await failing.StartAsync(); await Wait(() => failing.State == ConnectionState.RetryWaiting, "failed auto login enters retry state");
    Check(failing.RetryAt is { } at && (at - DateTimeOffset.Now).TotalSeconds is > 18 and <= 20, "retry scheduled exactly 20 seconds after failure");
    var attempts = mock.LoginAttempts.Count; profile.Client.ChatDraft = "unsent draft"; profile.DraftSettings["vars.serverMessage"] = "Draft saved while offline";
    mock.Settings["vars.serverName"] = "Remote changed"; mock.DropClients();
    await Wait(() => session.ConnectionLost && session.State == ConnectionState.RetryWaiting, "disconnect saves state and sets connection lost flag");
    var offlineMaps = new List<MapEntry> { new("MP_Abandoned", "ConquestLarge0", 5), new("MP_Damage", "RushLarge0", 2) };
    await session.ReplaceMapsAsync(offlineMaps);
    await Wait(() => session.IsConnected && mock.Settings["vars.serverName"] == "Local preferred", "reconnect restores local settings", 35);
    Check(mock.LoginAttempts.Count >= attempts + 2, "failed login retries without fixed cap"); Check(new ProfileStore(root).State.Servers[0].Client.ChatDraft == "unsent draft", "client drafts survive disconnect/reconnect");
    Check(mock.Settings["vars.serverMessage"] == "Draft saved while offline", "valid client setting drafts participate in reconnect synchronization");
    Check(mock.Maps.SequenceEqual(offlineMaps) && mock.DiskMaps.SequenceEqual(offlineMaps), "offline map edits remain locally preferred and save to disk after reconnect");
    await failing.StopAsync(); await session.StopAsync(); Console.WriteLine($"\n{checks} checks passed."); return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally
{
    if (root.StartsWith(Path.Combine(Path.GetTempPath(), "LightRcon-tests-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) Directory.Delete(root, true);
}
