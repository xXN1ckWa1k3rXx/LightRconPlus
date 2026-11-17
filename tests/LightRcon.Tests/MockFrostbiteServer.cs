using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LightRcon.Core;

namespace LightRcon.Tests;

public sealed class MockFrostbiteServer : IAsyncDisposable
{
    public const string Password = "MockRcon123";
    public string GameCode { get; set; } = "BF4";
    public ConcurrentDictionary<string, bool> Alive { get; } = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<int, TcpClient> clients = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> sendGates = new();
    private readonly ConcurrentBag<Task> workers = [];
    private readonly Task accepting;
    private int id;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public ConcurrentQueue<string[]> Received { get; } = new();
    public ConcurrentDictionary<string, string> Settings { get; } = new(SettingsCatalog.All.ToDictionary(x => x.Command, x => x.Kind == "bool" ? "true" : x.Kind == "number" ? "100" : x.Command == "vars.serverName" ? "LightRcon+ · BF4 Local Test" : x.Command == "vars.serverType" ? "RANKED" : x.Command == "vars.preset" ? "CUSTOM" : x.Command == "vars.unlockMode" ? "stats" : x.Command == "vars.gunMasterWeaponsPreset" ? "1" : "Mock value"));
    public bool PresetLocked { get; set; }
    public string GameMode { get; set; } = "ConquestLarge0";
    public string Map { get; set; } = "MP_Abandoned";
    public string[] TeamScores { get; set; } = ["700.000000", "650.000000"];
    public string TargetScore { get; set; } = "800";
    public List<MapEntry> Maps { get; } = [new("MP_Abandoned", "ConquestLarge0", 2), new("MP_Damage", "RushLarge0", 2)];
    public List<MapEntry> DiskMaps { get; } = [new("MP_Abandoned", "ConquestLarge0", 2), new("MP_Damage", "RushLarge0", 2)];
    public bool RejectMapLoad { get; set; }
    public bool RejectNextMap { get; set; }
    public int NextMapIndex { get; private set; }
    public int? LastSwitchedIndex { get; private set; }
    public int EventAcknowledgments;
    public ConcurrentQueue<DateTimeOffset> LoginAttempts { get; } = new();
    public MockFrostbiteServer() { listener.Start(); accepting = AcceptAsync(); }
    private async Task AcceptAsync()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); var number = Interlocked.Increment(ref id); clients[number] = client; workers.Add(ServeAsync(number, client)); } }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException) { }
    }
    private async Task ServeAsync(int number, TcpClient client)
    {
        using (client)
        {
            var gate = new SemaphoreSlim(1); sendGates[number] = gate;
            try
            {
                var stream = client.GetStream(); var authenticated = false; var salt = "00112233445566778899AABBCCDDEEFF";
                while (!stop.IsCancellationRequested)
                {
                    var packet = await FrostbitePacket.ReadAsync(stream, stop.Token);
                    if (packet.IsResponse && packet.OriginServer) { Interlocked.Increment(ref EventAcknowledgments); continue; }
                    if (packet.IsResponse || packet.OriginServer) throw new InvalidDataException("Client command origin flag must be ZERO (BF4/Procon actual wire format).");
                    var words = packet.Words; Received.Enqueue(words); var command = words[0]; string[] response;
                    if (command == "version") response = ["OK", GameCode, "mock"];
                    else if (command == "login.hashed" && words.Length == 1) { LoginAttempts.Enqueue(DateTimeOffset.Now); response = ["OK", salt]; }
                    else if (command == "login.hashed")
                    {
                        var hash = Convert.ToHexString(MD5.HashData(Convert.FromHexString(salt).Concat(Encoding.UTF8.GetBytes(Password)).ToArray())); authenticated = words.Length == 2 && words[1] == hash; response = [authenticated ? "OK" : "InvalidPasswordHash"];
                    }
                    else if (!authenticated) response = ["LoginRequired"];
                    else if (command == "admin.help") response = new[] { "OK" }.Concat(SettingsCatalog.All.Where(x => GameCode != "BF3" || x.Command is not ("vars.commander" or "vars.hacker" or "vars.maxSpectators" or "vars.preset")).Where(x => GameCode != "BFHL" || x.Command != "vars.commander").Select(x => x.Command)).Concat(new[] { "serverInfo", "admin.listPlayers", "mapList.list", "admin.eventsEnabled", "player.isAlive" }).ToArray();
                    else if (command == "player.isAlive") response = ["OK", Alive.GetValueOrDefault(words[1], true) ? "true" : "false"];
                    else if (command == "vars.preset")
                    {
                        if (words.Length == 1) response = ["OK", Settings[command], PresetLocked ? "true" : "false"];
                        else if (words.Length != 3 || !bool.TryParse(words[2], out var locked) || !new[] { "NORMAL", "CLASSIC", "HARDCORE", "INFANTRY", "CUSTOM" }.Contains(words[1])) response = ["InvalidArguments"];
                        else { Settings[command] = words[1]; PresetLocked = locked; Settings["vars.friendlyFire"] = "false"; response = ["OK"]; }
                    }
                    else if (Settings.ContainsKey(command))
                    {
                        if (words.Length > 1)
                        {
                            Settings[command] = words[1];
                            if (command == "vars.preset") Settings["vars.friendlyFire"] = "false";
                        }
                        response = words.Length > 1 ? ["OK"] : ["OK", Settings[command]];
                    }
                    else if (command == "serverInfo") response = ["OK", Settings["vars.serverName"], "3", "64", GameMode, Map, "0", "2", TeamScores.Length.ToString(), .. TeamScores, TargetScore, "InGame", "true", "true", "false", "93784", "150", "127.0.0.1:25200", "v1", "true"];
                    else if (command == "admin.listPlayers") response = ["OK", "11", "name", "guid", "teamId", "squadId", "kills", "deaths", "rank", "ping", "clanTag", "type", "kit", "3",
                        "Example_Alpha", "EA_11111111111111111111111111111111", "1", "2", "18", "6", "140", "32", "TEST", "Player", "Assault",
                        "Example_Bravo", "EA_22222222222222222222222222222222", "2", "3", "12", "9", "120", "48", "TEST", "Player", "Engineer",
                        "Example_Charlie", "EA_33333333333333333333333333333333", "1", "2", "7", "2", "90", "75", "", "Player", "Recon"];
                    else if (command == "mapList.list")
                    {
                        var offset = words.Length > 1 ? int.Parse(words[1]) : 0;
                        lock (Maps) { var page = Maps.Skip(offset).Take(100).ToArray(); response = new[] { "OK", page.Length.ToString(), "3" }.Concat(page.SelectMany(x => new[] { x.FileName, x.GameMode, x.Rounds.ToString() })).ToArray(); }
                    }
                    else if (command == "mapList.clear") { lock (Maps) Maps.Clear(); response = ["OK"]; }
                    else if (command == "mapList.save")
                    {
                        lock (Maps) { DiskMaps.Clear(); DiskMaps.AddRange(Maps); }
                        response = ["OK"];
                    }
                    else if (command == "mapList.load")
                    {
                        lock (Maps) { Maps.Clear(); if (!RejectMapLoad) Maps.AddRange(DiskMaps); }
                        response = RejectMapLoad ? ["InvalidMap"] : ["OK"];
                    }
                    else if (command == "mapList.setNextMapIndex")
                    {
                        lock (Maps)
                        {
                            if (RejectNextMap || !int.TryParse(words.ElementAtOrDefault(1), out var index) || index < 0 || index >= Maps.Count) response = ["InvalidMapIndex"];
                            else { NextMapIndex = index; response = ["OK"]; }
                        }
                    }
                    else if (command == "mapList.runNextRound")
                    {
                        lock (Maps)
                        {
                            if (NextMapIndex < 0 || NextMapIndex >= Maps.Count) response = ["InvalidMapIndex"];
                            else { LastSwitchedIndex = NextMapIndex; Map = Maps[NextMapIndex].FileName; GameMode = Maps[NextMapIndex].GameMode; response = ["OK"]; }
                        }
                    }
                    else if (command == "mapList.add")
                    {
                        if (words[1] == "INVALID_MAP") response = ["InvalidMap"];
                        else { lock (Maps) Maps.Add(new(words[1], words[2], int.Parse(words[3]))); response = ["OK"]; }
                    }
                    else if (command == "banList.list") response = words.ElementAtOrDefault(1) is null or "0" ? ["OK", "guid", "EA_44444444444444444444444444444444", "perm", "0", "0", "Example permanent ban", "name", "Example_Banned", "seconds", "1800", "0", "Example temporary ban"] : ["OK"];
                    else if (command == "reservedSlotsList.list") response = words.ElementAtOrDefault(1) is null or "0" ? ["OK", "Example_Reserved"] : ["OK"];
                    else if (command == "spectatorList.list") response = GameCode == "BF3" ? ["UnknownCommand"] : ["OK", "Example_Spectator"];
                    else if (command == "admin.password") response = ["OK", Password];
                    else response = ["OK"];
                    await Send(stream, packet.Sequence, false, true, response, gate);
                    if (command == "admin.eventsEnabled" && authenticated)
                    {
                        await Send(stream, 77, true, false, ["player.onChat", "Example_Alpha", "Local protocol test: hello 世界", "all"], gate);
                        await Send(stream, 78, true, false, ["player.onJoin", "Example_Charlie", "EA_33333333333333333333333333333333"], gate);
                        await Send(stream, 81, true, false, ["player.onChat", "Example_Bravo", "ID_CHAT_REQUEST_MEDIC", "team", "2"], gate);
                        await Send(stream, 83, true, false, ["player.onChat", "Example_Bravo", "ID_CHAT_ATTACK/DEFEND", "team", "2"], gate);
                        await Send(stream, 82, true, false, ["player.onKill", "Example_Alpha", "Example_Bravo", "U_AEK971", "false"], gate);
                    }
                    if (command == "punkBuster.pb_sv_command") await Send(stream, 79, true, false, ["punkBuster.onMessage", "PunkBuster Server: 1 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa(VALID) 127.0.0.1:3659 OK 1 3.0 0 (W) \"Example_Alpha\""], gate);
                    if (command == "admin.say") await Send(stream, 80, true, false, ["player.onChat", "Server", words[1], "all"], gate);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
            finally { clients.TryRemove(number, out _); sendGates.TryRemove(number, out _); }
        }
    }
    private async Task Send(Stream stream, uint sequence, bool server, bool response, string[] words, SemaphoreSlim gate)
    {
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
        writer.Write(sequence | (server ? 0x80000000u : 0) | (response ? 0x40000000u : 0)); writer.Write(0); writer.Write(words.Length);
        foreach (var word in words) { var bytes = Encoding.UTF8.GetBytes(word); writer.Write(bytes.Length); writer.Write(bytes); writer.Write((byte)0); }
        var packet = buffer.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), packet.Length); await gate.WaitAsync(stop.Token);
        try { await stream.WriteAsync(packet.AsMemory(0, 7), stop.Token); await stream.WriteAsync(packet.AsMemory(7), stop.Token); } finally { gate.Release(); }
    }
    public void DropClients() { foreach (var client in clients.Values) client.Close(); }
    public async Task EmitAsync(params string[] words)
    {
        if (words[0] == "player.onKill") Alive[words[2]] = false;
        if (words[0] is "player.onSpawn" or "player.spawn") Alive[words[1]] = true;
        foreach (var client in clients)
        {
            if (sendGates.TryGetValue(client.Key, out var eventGate))
                await Send(client.Value.GetStream(), 900, true, false, words, eventGate);
        }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); DropClients(); await accepting; await Task.WhenAll(workers); stop.Dispose(); }
}
