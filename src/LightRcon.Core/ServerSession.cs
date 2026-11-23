using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace LightRcon.Core;

public sealed class ServerSession : IAsyncDisposable
{
    private readonly ProfileStore store;
    private readonly TimeSpan retryDelay;
    private readonly SemaphoreSlim operationGate = new(1);
    private readonly SemaphoreSlim refreshGate = new(1);
    private readonly object dataGate = new();
    private readonly List<ConsoleEntry> console = [];
    private readonly HashSet<Guid> importedPluginConsole = [];
    private readonly Queue<Guid> importedPluginConsoleOrder = [];
    private readonly HashSet<string> unsupported = [];
    private readonly HashSet<string> commands = [];
    private readonly Dictionary<string, string> remoteSettings = [];
    private readonly Dictionary<string, bool> lifeStates = [];
    private readonly List<(string Text, DateTimeOffset Sent)> adminEchoes = [];
    private readonly HashSet<string> lifeQueries = [];
    private CancellationTokenSource? runCancellation;
    private Task? runner;
    private TaskCompletionSource retryRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ICommandConnection? connection;
    private LayerServer? layerServer;
    private readonly SessionLogWriter textLogs;
    private bool everConnected;
    private long version;
    private long mapsRevision;
    public event Action<LayerMapUpdate>? MapsChanged;
    public ServerProfile Profile { get; }
    public ActivityJournal Journal { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Offline;
    private string statusKey = "Not connected";
    public string Status => statusKey == "Connection failed; retrying in {0} seconds"
        ? UiText.Format(statusKey, retryDelay.TotalSeconds) : UiText.Get(statusKey);
    public string LastError { get; private set; } = "";
    public string LastFailureStage { get; private set; } = "";
    public DateTimeOffset? RetryAt { get; private set; }
    public ServerInfo Info { get; private set; } = ServerInfo.Empty;
    public GameId Game => Profile.Game;
    public int RoundTeamCount => Math.Clamp(Info.TeamScores.Length > 0 ? Info.TeamScores.Length : Math.Max(2, PlayerSnapshot().Select(player => player.Team).DefaultIfEmpty(0).Max()), 1, 4);
    public LayerPermission Permissions { get; private set; } = LayerPermission.All;
    public bool IsConnected => State == ConnectionState.Connected;
    public bool ConnectionLost { get; private set; }
    public long Version => Interlocked.Read(ref version);
    public string LayerFingerprint => layerServer?.Fingerprint ?? "";
    public bool IsHosting => layerServer is not null;
    public int LayerClientCount => layerServer?.ClientCount ?? 0;
    public event Action<string[]>? RemoteEvent;
    public Plugins.PluginManager Plugins { get; }
    public string PluginDirectory { get; }
    public event Action<ConsoleEntry>? Logged;
    internal CancellationToken LifetimeToken => runCancellation?.Token ?? CancellationToken.None;
    public event Action? SaveClientRequested;
    public event Action? Changed;
    public List<PlayerInfo> Players { get; private set; } = [];
    public List<BanEntry> Bans { get; private set; } = [];
    public List<MapEntry> Maps { get; private set; } = [];
    public List<string> Spectators { get; private set; } = [];
    public List<string> Reserved { get; private set; } = [];
    public PlayerInfo[] PlayerSnapshot() { lock (dataGate) return Players.ToArray(); }
    public BanEntry[] BanSnapshot() { lock (dataGate) return Bans.ToArray(); }
    public MapEntry[] MapSnapshot() { lock (dataGate) return Maps.ToArray(); }
    public string[] SpectatorSnapshot() { lock (dataGate) return Spectators.ToArray(); }
    public string[] ReservedSnapshot() { lock (dataGate) return Reserved.ToArray(); }
    public ConsoleEntry[] ConsoleSnapshot() { lock (dataGate) return console.ToArray(); }
    public Dictionary<string, string> SettingsSnapshot() { lock (dataGate) return new(remoteSettings); }
    public bool IsSupported(string command) { lock (dataGate) return (SettingsCatalog.Find(command) is null || SettingsCatalog.Supports(command, Game)) && !unsupported.Contains(command); }

    public ServerSession(ServerProfile profile, ProfileStore store, TimeSpan? retryDelay = null, string? pluginDirectory = null)
    {
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(20);
        if (this.retryDelay <= TimeSpan.Zero || this.retryDelay > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(retryDelay));
        Profile = profile;
        this.store = store;
        PluginDirectory = Path.GetFullPath(pluginDirectory ?? LightRcon.Core.Plugins.PluginLibrary.DefaultRoot);
        textLogs = new(store);
        Plugins = new(this, store);
        Journal = new(store.ServerDirectory(profile.Id), profile.Layer.Enabled);
        Journal.Appended += _ => Touch();
        Journal.Appended += entries => { if (!Profile.IsLayer) foreach (var entry in entries) textLogs.WriteActivity(entry); };
        store.LogsCleared += Journal.ClearPersisted;
    }
    private void Touch() { Interlocked.Increment(ref version); Changed?.Invoke(); }
    public void Log(string kind, string text)
    {
        var entry = new ConsoleEntry(DateTimeOffset.Now, kind, text);
        lock (dataGate)
        {
            console.Add(entry);
            if (console.Count > 3000) console.RemoveRange(0, console.Count - 3000);
        }
        Touch();
        if (!Profile.IsLayer) textLogs.Write("events", $"[{DateTimeOffset.Now:O}] [{kind}] {text}");
        Logged?.Invoke(entry);
    }
    private void ImportPluginConsole(ConsoleEntry[] entries)
    {
        List<ConsoleEntry> fresh = [];
        lock (dataGate)
        {
            foreach (var entry in entries.Where(entry => entry.IsPluginConsole))
            {
                if (!importedPluginConsole.Add(entry.Id)) continue;
                importedPluginConsoleOrder.Enqueue(entry.Id); fresh.Add(entry); console.Add(entry);
            }
            while (importedPluginConsoleOrder.Count > 3000) importedPluginConsole.Remove(importedPluginConsoleOrder.Dequeue());
            if (console.Count > 3000) console.RemoveRange(0, console.Count - 3000);
        }
        if (fresh.Count == 0) return;
        Touch();
        foreach (var entry in fresh) Logged?.Invoke(entry);
    }
    private void SetState(ConnectionState state, string text)
    {
        State = state; statusKey = text; Touch();
    }
    public Task StartAsync()
    {
        if (runner is { IsCompleted: false })
        {
            if (State == ConnectionState.RetryWaiting) retryRequested.TrySetResult();
            return Task.CompletedTask;
        }
        runCancellation?.Dispose();
        runCancellation = new();
        runner = Task.Run(() => RunAsync(runCancellation.Token));
        return Task.CompletedTask;
    }
    public async Task StopAsync()
    {
        runCancellation?.Cancel();
        if (connection is not null) await connection.DisposeAsync();
        if (runner is not null) await runner;
        RetryAt = null;
        ConnectionLost = false;
        SetState(ConnectionState.Offline, "Disconnected");
        store.Save();
    }
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                LastError = ""; LastFailureStage = ""; RetryAt = null;
                SetState(ConnectionState.Connecting, "Connecting…");
                Permissions = LayerPermission.All;
                if (Profile.IsLayer) mapsRevision = 0;
                LayerSnapshot? initialSnapshot = null;
                if (Profile.IsLayer)
                {
                    var layer = new LayerConnection();
                    connection = layer;
                    layer.EventReceived += HandleEvent;
                    layer.HistoryReceived += Journal.Import;
                    layer.PluginConsoleReceived += ImportPluginConsole;
                    layer.PluginsReceived += Plugins.AcceptRemoteSnapshot;
                    layer.MapsReceived += AcceptRemoteMaps;
                    SetState(ConnectionState.Authenticating, "Authenticating with Layer…");
                    await layer.ConnectAsync(Profile, token);
                    Permissions = layer.Permissions;
                    initialSnapshot = await ReadLayerSnapshotAsync(true, token);
                    if (initialSnapshot is not null) SetGame(initialSnapshot.Game);
                    else
                    {
                        var version = await RawAsync(["version"], token);
                        if (version.Length < 2) throw new RconException("Missing game identifier in version response.");
                        SetGame(GameCatalog.Parse(version[1]));
                    }
                }
                else
                {
                    var rcon = new RconConnection();
                    connection = rcon;
                    rcon.EventReceived += HandleEvent;
                    SetState(ConnectionState.Authenticating, "Authenticating with RCON…");
                    await rcon.ConnectAsync(Profile.Host, Profile.Port, SecretProtection.Unprotect(Profile.ProtectedPassword), token, subscribeEvents: false);
                    SetGame(rcon.Game);
                    await rcon.ExecuteAsync(["admin.eventsEnabled", "true"], token);
                }
                SetState(ConnectionState.Synchronizing, "Comparing local settings…");
                // UI drafts and window preferences must be committed BEFORE reconciliation.
                SaveClientRequested?.Invoke();
                store.Save();
                lock (Profile.Sync)
                {
                    foreach (var draft in Profile.DraftSettings)
                    {
                        try { SettingsCatalog.Validate(draft.Key, draft.Value, Game); Profile.DesiredSettings[draft.Key] = draft.Value; }
                        catch (ArgumentException) { Log("Setting", UiText.Format("Invalid draft for {0}; not synchronized.", draft.Key)); }
                    }
                }
                lock (dataGate) { unsupported.Clear(); commands.Clear(); }
                if (initialSnapshot is not null) { lock (dataGate) commands.UnionWith(initialSnapshot.Commands); }
                else
                {
                    try { var help = await RawAsync(["admin.help"], token); lock (dataGate) commands.UnionWith(help.Skip(1)); }
                    catch (RconException) { }
                }
                await ReadSettingsAsync(token, initialSnapshot);
                await ReconcileSettingsAsync(token);
                await RefreshAsync(true, token);
                if (Profile.DesiredMaps is not null && Permissions.HasFlag(LayerPermission.Maps))
                {
                    List<MapEntry> desired; lock (Profile.Sync) desired = Profile.DesiredMaps.ToList();
                    if (!desired.SequenceEqual(MapSnapshot())) await ReplaceMapsCoreAsync(desired, token);
                }
                everConnected = true;
                if (!Profile.IsLayer)
                {
                    textLogs.Begin(Info.Name.Length > 0 ? Info.Name : Profile.Name, Journal.Snapshot);
                }
                ConnectionLost = false;
                SetState(ConnectionState.Connected, Profile.IsLayer ? "Layer connected" : "RCON connected");
                Log("Connection", UiText.Get("Authenticated; local settings compared."));
                if (Profile.Layer.Enabled && layerServer is null)
                {
                    try { await StartLayerAsync(); }
                    catch (Exception ex) { Log("Layer", UiText.Get("Listener failed: ") + ex.Message); }
                }
                await Plugins.RestoreAsync();
                using var polling = CancellationTokenSource.CreateLinkedTokenSource(token);
                var monitor = PollAsync(polling.Token);
                await Task.WhenAny(connection.Completion, monitor);
                polling.Cancel();
                try { await monitor; } catch (OperationCanceledException) { }
                if (!token.IsCancellationRequested) throw connection.Failure ?? new IOException(UiText.Get("Remote connection closed."));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                LastFailureStage = Status;
                LastError = ConnectionFailure.Describe(ex);
                Log("Error", LastFailureStage + ": " + LastError);
                if (everConnected)
                {
                    ConnectionLost = true;
                    SaveClientRequested?.Invoke();
                    store.Save();
                }
            }
            finally
            {
                await Plugins.StopAsync();
                if (connection is not null) { await connection.DisposeAsync(); connection = null; }
            }
            if (token.IsCancellationRequested) break;
            if (!Profile.AutoLogin && !everConnected)
            {
                SetState(ConnectionState.Offline, "Connection failed");
                return;
            }
            retryRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RetryAt = DateTimeOffset.Now + retryDelay;
            SetState(ConnectionState.RetryWaiting, everConnected ? "Connection to the remote server has been lost" : "Connection failed; retrying in {0} seconds");
            try { await retryRequested.Task.WaitAsync(retryDelay, token); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { break; }
        }
        SetState(ConnectionState.Offline, "Disconnected");
    }
    private async Task PollAsync(CancellationToken token)
    {
        var count = 0;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            await RefreshAsync(++count % 6 == 0, token);
        }
    }
    internal async Task<LayerFrame> RequestPluginsAsync(string operation, params string[] arguments)
    {
        if (connection is not LayerConnection layer || !IsConnected)
            throw new IOException(UiText.Get("Connect to the upstream Layer first."));
        return await layer.RequestAsync(new() { Type = operation, Words = arguments }, LifetimeToken);
    }
    private void SetGame(GameId game)
    {
        lock (Profile.Sync)
        {
            if (Profile.Game != game)
            {
                Profile.DesiredMaps = null; Profile.DesiredSettings.Clear(); Profile.DraftSettings.Clear();
                Profile.Game = game;
            }
            foreach (var key in Profile.DesiredSettings.Keys.Where(key => !SettingsCatalog.Supports(key, game)).ToArray()) Profile.DesiredSettings.Remove(key);
            foreach (var key in Profile.DraftSettings.Keys.Where(key => !SettingsCatalog.Supports(key, game)).ToArray()) Profile.DraftSettings.Remove(key);
        }
        lock (dataGate) { lifeStates.Clear(); lifeQueries.Clear(); adminEchoes.Clear(); remoteSettings.Clear(); }
        store.Save();
    }
    private async Task<string[]> RawAsync(string[] words, CancellationToken token = default)
    {
        var active = connection ?? throw new IOException(UiText.Get("No active connection."));
        return await active.ExecuteAsync(words, token);
    }
    public async Task<string[]> ExecuteAsync(string[] words, CancellationToken token = default)
        => await ExecuteAsAsync(words, null, token);
    internal Task<string[]> ExecutePluginAsync(string[] words, CancellationToken token = default)
        => ExecuteAsAsync(words, null, token, isPluginMessage: true);
    internal async Task<string[]> ExecuteAsAsync(string[] words, string? layerActor, CancellationToken token = default, bool isPluginMessage = false)
    {
        if (!IsConnected) throw new IOException(UiText.Get("Server not connected; action not sent."));
        if (words.Length == 0) throw new ArgumentException("Empty command.");
        if (words[0] == "mapList.endRound")
        {
            if (words.Length != 2 || !int.TryParse(words[1], out var team) || team < 1 || team > RoundTeamCount)
                throw new ArgumentException(UiText.Get("Select a valid winning team."));
        }
        var settingKey = SettingsCatalog.SettingKey(words);
        if (words[0] == "vars.teamFactionOverride")
        {
            if (Game != GameId.BF4 || words.Length != 1 && (words.Length != 3 || !SettingsCatalog.IsFactionOverride(settingKey)))
                throw new ArgumentException("Invalid faction override command for this game.");
            if (words.Length == 3) SettingsCatalog.Validate(settingKey, words[2], Game);
        }
        if (SettingsCatalog.Find(settingKey) is not null && !SettingsCatalog.Supports(settingKey, Game))
            throw new ArgumentException(UiText.Format("Setting {0} is not available for {1}.", words[0], Game));
        if (words[0] is "login.hashed" or "login.plainText" or "logout" or "quit" or "admin.eventsEnabled" or "admin.password")
            throw new UnauthorizedAccessException(UiText.Get("Login, subscriptions and the RCON password are managed by the connection manager and cannot be changed through the console."));
        await operationGate.WaitAsync(token);
        try
        {
            var adminChat = !Profile.IsLayer && (words[0] is "admin.say" or "admin.yell") && words.Length >= 3;
            var wireWords = words;
            var speaker = layerActor ?? "Admin";
            var originalMessage = adminChat ? words[1] : "";
            if (adminChat && layerActor is not null)
            {
                wireWords = (string[])words.Clone();
                wireWords[1] = "[" + layerActor + "] " + originalMessage;
                var limit = words[0] == "admin.yell" ? 256 : 128;
                if (wireWords[1].Length >= limit)
                    throw new ArgumentException(UiText.Format("{0} messages must be shorter than {1} characters.", words[0] == "admin.yell" ? "Yell" : "Say", limit));
            }
            if (adminChat)
                lock (dataGate)
                {
                    adminEchoes.RemoveAll(item => item.Sent < DateTimeOffset.Now.AddSeconds(-15));
                    adminEchoes.Add((wireWords[1], DateTimeOffset.Now));
                }
            string[] result;
            try { result = await RawAsync(wireWords, token); }
            catch
            {
                if (adminChat) lock (dataGate) { var index = adminEchoes.FindIndex(item => item.Text == wireWords[1]); if (index >= 0) adminEchoes.RemoveAt(index); }
                throw;
            }
            var sensitive = words[0] is "vars.gamePassword";
            Log("RCON", "> " + (sensitive ? words[0] + " [redacted]" : string.Join(" ", words.Select(CommandText.Quote))));
            Log("RCON", sensitive ? "< [redacted]" : "< " + string.Join(" | ", result));
            if (adminChat)
            {
                var audienceIndex = words[0] == "admin.yell" ? 3 : 2;
                Journal.Add("Chat", speaker, string.Join(" ", words.Skip(audienceIndex)), originalMessage, game: Game,
                    isAdminMessage: true, adminMessageType: words[0] == "admin.yell" ? "Yell" : "Say", isPluginMessage: isPluginMessage);
            }
            if ((words.Length == 2 || words[0] == "vars.preset" && words.Length == 3 || SettingsCatalog.IsFactionOverride(settingKey) && words.Length == 3) && SettingsCatalog.Find(settingKey) is not null)
            {
                var value = words[0] == "vars.preset" ? words[1] + " " + (bool.TryParse(words.ElementAtOrDefault(2), out var locked) && locked ? "true" : "false") : words[^1];
                lock (Profile.Sync) Profile.DesiredSettings[settingKey] = value;
                lock (dataGate) remoteSettings[settingKey] = value;
                store.Save();
                if (!Profile.IsLayer && words[0] is "vars.preset" or "vars.serverType") await ReadSettingsAsync(token);
            }
            if (words[0] is "mapList.add" or "mapList.remove" or "mapList.clear" or "mapList.load")
            {
                var maps = await ReadMapsAsync(token);
                AdoptMaps(maps);
            }
            return result;
        }
        finally { operationGate.Release(); }
    }
    public async Task EndRoundAsync(int winningTeam, CancellationToken token = default)
    {
        LayerAuthorization.Demand(Permissions, LayerPermission.Maps);
        await ExecuteAsync(["mapList.endRound", winningTeam.ToString(System.Globalization.CultureInfo.InvariantCulture)], token);
        Log("Round", UiText.Format("Round ended. Winning team: {0}.", winningTeam));
    }
    public async Task<string[]> ExecuteConsoleAsync(string[] words, CancellationToken token = default)
    {
        if (!Permissions.HasFlag(LayerPermission.Console)) throw new UnauthorizedAccessException(UiText.Get("You do not have console permission."));
        if (connection is LayerConnection layer)
        {
            if (!IsConnected) throw new IOException(UiText.Get("Layer not connected."));
            var result = (await layer.RequestAsync(new() { Type = "console", Words = words }, token)).Words;
            Log("RCON", "> " + string.Join(" ", words)); Log("RCON", "< " + string.Join(" | ", result));
            return result;
        }
        return await ExecuteAsync(words, token);
    }
    internal LayerSnapshot CreateLayerSnapshot(bool settingsOnly)
    {
        if (!IsConnected) throw new IOException(UiText.Get("Server not connected; action not sent."));
        lock (dataGate) return new()
        {
            Game = Game, Info = Info,
            // Players have mutable event/PunkBuster fields: detach them from the live list.
            Players = settingsOnly ? [] : Players.Select(player => player.Copy()).ToArray(),
            Maps = settingsOnly ? [] : Maps.ToArray(),
            MapsRevision = mapsRevision,
            Settings = new(remoteSettings), Unsupported = unsupported.ToArray(), Commands = commands.ToArray()
        };
    }
    private async Task<LayerSnapshot?> ReadLayerSnapshotAsync(bool settingsOnly, CancellationToken token)
    {
        if (connection is not LayerConnection { SupportsSnapshots: true } layer) return null;
        var snapshot = (await layer.RequestAsync(new() { Type = "session.snapshot", Words = [settingsOnly ? "settings" : "state"] }, token)).Snapshot;
        // Oversized snapshots fall back to the existing paged commands. Older hosts never advertise this operation.
        if (snapshot is null) layer.SupportsSnapshots = false;
        return snapshot;
    }
    private void RememberSetting(string command, string value)
    {
        lock (dataGate) remoteSettings[command] = value;
        lock (Profile.Sync)
        {
            Profile.DesiredSettings.TryAdd(command, SettingsCatalog.IsFactionOverride(command) ? "default" : value);
            if (command == "vars.preset" && !Profile.DesiredSettings[command].Contains(' '))
                Profile.DesiredSettings[command] += " " + (SettingsCatalog.ParsePreset(value).Locked ? "true" : "false");
        }
    }
    private async Task ReadSettingsAsync(CancellationToken token, LayerSnapshot? snapshot = null)
    {
        snapshot ??= await ReadLayerSnapshotAsync(true, token);
        if (snapshot is not null)
        {
            lock (dataGate)
            {
                unsupported.Clear(); unsupported.UnionWith(snapshot.Unsupported);
                commands.Clear(); commands.UnionWith(snapshot.Commands);
                remoteSettings.Clear();
            }
            foreach (var item in snapshot.Settings.Where(item => SettingsCatalog.Supports(item.Key, Game) && SettingsCatalog.Find(item.Key) is not null))
                RememberSetting(item.Key, item.Value);
            store.Save(); Touch(); return;
        }
        if (Game == GameId.BF4)
        {
            try
            {
                var factions = await RawAsync(["vars.teamFactionOverride"], token);
                if (factions.Length != 5) throw new RconException("Invalid faction override response.");
                for (var team = 1; team <= 4; team++) RememberSetting("vars.teamFactionOverride " + team, factions[team]);
            }
            catch (RconException) { lock (dataGate) foreach (var team in Enumerable.Range(1, 4)) unsupported.Add("vars.teamFactionOverride " + team); }
        }
        await Parallel.ForEachAsync(SettingsCatalog.For(Game).Where(definition => !SettingsCatalog.IsFactionOverride(definition.Command)), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (definition, ct) =>
        {
            bool advertised; lock (dataGate) advertised = commands.Count == 0 || commands.Contains(definition.Command);
            if (!advertised) { lock (dataGate) unsupported.Add(definition.Command); return; }
            try
            {
                var result = await RawAsync([definition.Command], ct);
                if (result.Length < 2) return;
                var value = result[1];
                if (definition.Command == "vars.preset") value += " " + (bool.TryParse(result.ElementAtOrDefault(2), out var locked) && locked ? "true" : "false");
                RememberSetting(definition.Command, value);
            }
            catch (RconException ex) { lock (dataGate) unsupported.Add(definition.Command); Log("Setting", definition.Command + ": " + ex.Code); }
        });
        store.Save();
        Touch();
    }
    internal bool TryReadSetting(string[] words, out string[] result)
    {
        result = [];
        if (!IsConnected || words.Length != 1 || SettingsCatalog.Find(words[0]) is null && words[0] != "vars.teamFactionOverride") return false;
        lock (dataGate)
        {
            if (words[0] == "vars.teamFactionOverride" && Enumerable.Range(1, 4).All(team => remoteSettings.ContainsKey("vars.teamFactionOverride " + team)))
            { result = ["OK", .. Enumerable.Range(1, 4).Select(team => remoteSettings["vars.teamFactionOverride " + team])]; return true; }
            if (!remoteSettings.TryGetValue(words[0], out var value)) return false;
            result = words[0] == "vars.preset" ? ["OK", .. value.Split(' ')] : ["OK", value];
            return true;
        }
    }
    private async Task ReconcileSettingsAsync(CancellationToken token)
    {
        if (!Permissions.HasFlag(LayerPermission.Settings)) return;
        Dictionary<string, string> desired;
        lock (Profile.Sync) desired = new(Profile.DesiredSettings);
        var actual = SettingsSnapshot();
        // Preset first: applying it can reset individual gameplay values.
        foreach (var item in desired.OrderBy(x => x.Key == "vars.serverType" ? 0 : x.Key == "vars.preset" ? 1 : 2))
        {
            if (SettingsCatalog.IsFactionOverride(item.Key) && item.Value == "default") continue;
            if (!IsSupported(item.Key) || !actual.TryGetValue(item.Key, out var remote) || SettingsCatalog.Equal(remote, item.Value)) continue;
            try
            {
                await RawAsync(SettingsCatalog.SetCommand(item.Key, item.Value), token);
                lock (dataGate) remoteSettings[item.Key] = item.Value;
                Log("Sync", UiText.Format("Local value applied: {0}", item.Key));
                if (item.Key is "vars.preset" or "vars.serverType")
                {
                    await ReadSettingsAsync(token);
                    actual = SettingsSnapshot();
                }
            }
            catch (RconException ex) { Log("Sync", UiText.Format("Failed to sync {0}; local value retained: {1}", item.Key, ex.Code)); }
        }
        store.Save();
    }
    public async Task ApplySettingsAsync(Dictionary<string, string> settings, CancellationToken token = default)
    {
        if (!Permissions.HasFlag(LayerPermission.Settings)) throw new UnauthorizedAccessException(UiText.Get("No permission to change server settings."));
        foreach (var entry in settings) SettingsCatalog.Validate(entry.Key, entry.Value, Game);
        lock (Profile.Sync) foreach (var entry in settings) Profile.DesiredSettings[entry.Key] = entry.Value;
        store.Save();
        if (!IsConnected) { Log("Setting", UiText.Get("Local settings saved; synchronization will resume after reconnecting.")); return; }
        await operationGate.WaitAsync(token);
        try
        {
            // Query again: presets and other administrators may have changed the server.
            await ReadSettingsAsync(token);
            await ReconcileSettingsAsync(token);
        }
        finally { operationGate.Release(); }
        await ReadSettingsAsync(token);
    }
    public async Task RefreshAsync(bool full = true, CancellationToken token = default)
    {
        await refreshGate.WaitAsync(token);
        var acquired = false;
        try
        {
            await operationGate.WaitAsync(token);
            acquired = true;
            var snapshot = await ReadLayerSnapshotAsync(false, token);
            var info = snapshot?.Info ?? FrostbiteParser.Server(await RawAsync(["serverInfo"], token)) with { Game = Game };
            var players = snapshot?.Players.ToList() ?? FrostbiteParser.Players(await RawAsync(["admin.listPlayers", "all"], token), PlayerSnapshot());
            // Seed once per observed player; thereafter spawn/kill events maintain life state.
            // Bound concurrency so a full server does not create an unbounded request burst.
            if (snapshot is null) await Parallel.ForEachAsync(players, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (player, ct) =>
            {
                bool query;
                lock (dataGate) query = !lifeStates.ContainsKey(player.Name) && lifeQueries.Add(player.Name) && (commands.Count == 0 || commands.Contains("player.isAlive"));
                if (!query) return;
                try
                {
                    var result = await RawAsync(["player.isAlive", player.Name], ct);
                    if (bool.TryParse(result.ElementAtOrDefault(1), out var alive)) lock (dataGate) lifeStates.TryAdd(player.Name, alive);
                }
                catch (RconException) { /* Unsupported or player left; unknown is not dead. */ }
            });
            lock (dataGate)
            {
                foreach (var player in players)
                    if (lifeStates.TryGetValue(player.Name, out var alive)) player.IsAlive = alive;
                Info = info; Players = players;
            }
            // Keep rotations current on the five-second poll too, including changes made
            // outside LightRcon. Layer pushes cover host/client edits immediately.
            if (snapshot is not null) AcceptRemoteMaps(new(snapshot.MapsRevision, snapshot.Maps));
            else ObserveMaps(await ReadMapsAsync(token));
            if (!full) { Touch(); return; }
            if (snapshot is not null) await ReadSettingsAsync(token, snapshot);
            if (IsConnected && !Profile.IsLayer) await ReadSettingsAsync(token);
            await OptionalRefreshAsync(async () =>
            {
                List<BanEntry> bans = [];
                for (var offset = 0; offset < 100000;)
                {
                    var page = FrostbiteParser.Bans(await RawAsync(["banList.list", offset.ToString()], token), offset);
                    bans.AddRange(page);
                    if (page.Count == 0) break;
                    offset += page.Count;
                }
                lock (dataGate) Bans = bans;
            });
            await OptionalRefreshAsync(async () =>
            {
                List<string> reserved = [];
                for (var offset = 0; offset < 10000;)
                {
                    var page = (await RawAsync(["reservedSlotsList.list", offset.ToString()], token)).Skip(1).ToList();
                    reserved.AddRange(page);
                    if (page.Count == 0) break;
                    offset += page.Count;
                }
                lock (dataGate) Reserved = reserved;
            });
            await OptionalRefreshAsync(async () =>
            {
                var names = (await RawAsync(["spectatorList.list"], token)).Skip(1).ToList();
                lock (dataGate) Spectators = names;
            });
            if (!Profile.IsLayer) await OptionalRefreshAsync(async () => { await RawAsync(["punkBuster.pb_sv_command", "pb_sv_plist"], token); });
            store.Save();
            Touch();
        }
        finally { if (acquired) operationGate.Release(); refreshGate.Release(); }
    }
    private async Task OptionalRefreshAsync(Func<Task> action)
    {
        try { await action(); } catch (RconException ex) { Log("Refresh", ex.Code); }
    }
    private async Task<List<MapEntry>> ReadMapsAsync(CancellationToken token)
    {
        List<MapEntry> maps = [];
        for (var offset = 0; offset < 10000;)
        {
            var page = FrostbiteParser.Maps(await RawAsync(["mapList.list", offset.ToString()], token)).Select(x => x with { Game = Game }).ToList();
            maps.AddRange(page);
            if (page.Count == 0) break;
            offset += page.Count;
        }
        return maps;
    }
    private void AdoptMaps(List<MapEntry> maps)
    {
        lock (Profile.Sync) Profile.DesiredMaps = maps.ToList();
        ObserveMaps(maps);
        store.Save(); Touch();
    }
    private void ObserveMaps(List<MapEntry> maps)
    {
        MapEntry[] previous;
        LayerMapUpdate? update = null;
        lock (dataGate)
        {
            previous = Maps.ToArray();
            if (!Maps.SequenceEqual(maps)) { Maps = maps.ToList(); update = new(++mapsRevision, Maps.ToArray()); }
        }
        lock (Profile.Sync)
            if (Profile.DesiredMaps is null || IsConnected && Profile.DesiredMaps.SequenceEqual(previous)) Profile.DesiredMaps = maps.ToList();
        if (update is not null) { MapsChanged?.Invoke(update); Touch(); }
    }
    private void AcceptRemoteMaps(LayerMapUpdate update)
    {
        MapEntry[] previous;
        lock (dataGate)
        {
            if (connection is LayerConnection { SupportsMapUpdates: true } && update.Revision < mapsRevision) return;
            previous = Maps.ToArray(); mapsRevision = update.Revision; Maps = update.Entries.ToList();
        }
        lock (Profile.Sync)
            if (Profile.DesiredMaps is null || IsConnected && Profile.DesiredMaps.SequenceEqual(previous)) Profile.DesiredMaps = update.Entries.ToList();
        if (!previous.SequenceEqual(update.Entries)) store.Save();
        Touch();
    }
    public async Task RefreshMapsAsync(CancellationToken token = default)
    {
        await operationGate.WaitAsync(token);
        try
        {
            if (!IsConnected) throw new IOException(UiText.Get("Server not connected; action not sent."));
            AdoptMaps(await ReadMapsAsync(token));
        }
        finally { operationGate.Release(); }
    }
    public async Task LoadMapsFromDiskAsync(CancellationToken token = default)
    {
        if (!Permissions.HasFlag(LayerPermission.Maps)) throw new UnauthorizedAccessException(UiText.Get("No map management permission."));
        await operationGate.WaitAsync(token);
        try
        {
            if (!IsConnected) throw new IOException(UiText.Get("Server not connected; action not sent."));
            var original = await ReadMapsAsync(token);
            try
            {
                await RawAsync(["mapList.load"], token);
                AdoptMaps(await ReadMapsAsync(token));
                Log("Map List", UiText.Get("Loaded the server MapList.txt."));
            }
            catch
            {
                // BF4 documents an undefined runtime rotation on load failure.
                // Restore the live list without overwriting the source file.
                try { await WriteMapsAsync(original, token, save: false); }
                catch (Exception ex) { Log("Map List", UiText.Get("Restore failed; local list will be retried after reconnecting: ") + ex.Message); }
                throw;
            }
        }
        finally { operationGate.Release(); }
    }
    public Task SetNextMapAsync(int index, IReadOnlyList<MapEntry> expected, CancellationToken token = default)
        => SelectMapAsync(index, expected, false, token);
    public Task SwitchMapAsync(int index, IReadOnlyList<MapEntry> expected, CancellationToken token = default)
        => SelectMapAsync(index, expected, true, token);
    private async Task SelectMapAsync(int index, IReadOnlyList<MapEntry> expected, bool runNow, CancellationToken token)
    {
        if (!Permissions.HasFlag(LayerPermission.Maps)) throw new UnauthorizedAccessException(UiText.Get("No map management permission."));
        var snapshot = expected.ToArray();
        if (index < 0 || index >= snapshot.Length) throw new ArgumentException(UiText.Get("The map list changed. Select the map again."));
        await operationGate.WaitAsync(token);
        try
        {
            if (!IsConnected) throw new IOException(UiText.Get("Server not connected; action not sent."));
            var actual = await ReadMapsAsync(token);
            if (!actual.SequenceEqual(snapshot))
            {
                AdoptMaps(actual);
                throw new ArgumentException(UiText.Get("The map list changed. Select the map again."));
            }
            await RawAsync(["mapList.setNextMapIndex", index.ToString(System.Globalization.CultureInfo.InvariantCulture)], token);
            if (runNow) await RawAsync(["mapList.runNextRound"], token);
            Log("Map List", UiText.Format(runNow ? "Switching to {0} — {1}." : "Next map: {0} — {1}.", snapshot[index].Name, snapshot[index].ModeName));
            Touch();
        }
        finally { operationGate.Release(); }
    }
    public async Task ReplaceMapsAsync(List<MapEntry> maps, CancellationToken token = default)
        => await MutateMapsAsync(_ => maps.ToList(), token);
    public async Task MutateMapsAsync(Func<List<MapEntry>, List<MapEntry>> mutation, CancellationToken token = default, IReadOnlyList<MapEntry>? expectedCurrent = null)
    {
        if (!Permissions.HasFlag(LayerPermission.Maps)) throw new UnauthorizedAccessException(UiText.Get("No map management permission."));
        await operationGate.WaitAsync(token);
        try
        {
            List<MapEntry> current;
            lock (Profile.Sync) current = Profile.DesiredMaps?.ToList() ?? MapSnapshot().ToList();
            if (expectedCurrent is not null)
            {
                var actual = await ReadMapsAsync(token);
                if (!actual.SequenceEqual(expectedCurrent)) { AdoptMaps(actual); throw new ArgumentException(UiText.Get("The map list changed. Select the map again.")); }
                current = actual;
            }
            var maps = mutation(current.ToList());
            if (maps.Count > 1000 || maps.Any(x => x.Rounds is < 1 or > 100 || string.IsNullOrWhiteSpace(x.FileName) || string.IsNullOrWhiteSpace(x.GameMode)))
                throw new ArgumentException(UiText.Get("Invalid map or round count."));
            lock (Profile.Sync) Profile.DesiredMaps = maps.ToList();
            store.Save();
            if (!IsConnected) { lock (dataGate) Maps = maps.ToList(); Touch(); return; }
            try { await ReplaceMapsCoreAsync(maps, token, current); }
            catch (RconException)
            {
                // A rejected map must not poison every later addition and reconnect attempt.
                lock (Profile.Sync) Profile.DesiredMaps = connection is LayerConnection { SupportsMapUpdates: true } ? MapSnapshot().ToList() : current;
                store.Save(); Touch(); throw;
            }
        }
        finally { operationGate.Release(); }
    }
    private async Task ReplaceMapsCoreAsync(List<MapEntry> desired, CancellationToken token, IReadOnlyList<MapEntry>? expected = null)
    {
        if (connection is LayerConnection { SupportsMapUpdates: true } layer)
        {
            // The host owns the transaction and rollback. A rejected stale edit must
            // never trigger a second client-side rewrite over another administrator.
            await layer.RequestAsync(new() { Type = "maps.replace", Maps = new(0, desired.ToArray()), ExpectedMaps = expected?.ToArray() ?? MapSnapshot() }, token);
            // Ordered pushes remain authoritative if another administrator edits
            // again before this request's continuation runs on the client.
            lock (Profile.Sync) Profile.DesiredMaps = MapSnapshot().ToList();
            store.Save(); Touch();
            return;
        }
        var original = await ReadMapsAsync(token);
        try
        {
            await WriteMapsAsync(desired, token);
            AdoptMaps(desired);
            Log("Map List", UiText.Format("Synchronized and saved {0} maps.", desired.Count));
            Touch();
        }
        catch
        {
            try { await WriteMapsAsync(original, token); Log("Map List", UiText.Get("Map synchronization failed; restored the server rotation.")); }
            catch (Exception ex) { Log("Map List", UiText.Get("Restore failed; local list will be retried after reconnecting: ") + ex.Message); }
            throw;
        }
    }
    private async Task WriteMapsAsync(List<MapEntry> maps, CancellationToken token, bool save = true)
    {
        await RawAsync(["mapList.clear"], token);
        foreach (var map in maps) await RawAsync(["mapList.add", map.FileName, map.GameMode, map.Rounds.ToString()], token);
        if (save) await RawAsync(["mapList.save"], token);
    }
    public async Task StartLayerAsync()
    {
        if (layerServer is not null) return;
        var host = new LayerServer(this, store);
        try
        {
            await host.StartAsync();
            layerServer = host;
            lock (Profile.Sync) Profile.Layer.Enabled = true;
            Journal.SetPersistent(true);
            store.Save();
            Log("Layer", UiText.Format("TLS Layer listening on {0}:{1}", Profile.Layer.BindAddress, Profile.Layer.Port));
            Touch();
        }
        catch { await host.DisposeAsync(); throw; }
    }
    public async Task StopLayerAsync()
    {
        if (layerServer is not null) { await layerServer.DisposeAsync(); layerServer = null; }
        lock (Profile.Sync) Profile.Layer.Enabled = false;
        Journal.SetPersistent(false);
        store.Save(); Touch();
    }
    private async Task RefreshLayerAccountsAsync()
    {
        layerServer?.RevalidateClients(); store.Save(); Touch();
        await Plugins.RefreshAccountsAsync();
    }
    public async Task<PublicLayerOptions> GetLayerOptionsAsync(bool upstream)
    {
        if (upstream)
        {
            if (connection is not LayerConnection layer || !IsConnected) throw new IOException(UiText.Get("Connect to the upstream Layer first."));
            return (await layer.RequestAsync(new() { Type = "layer.get" })).Layer ?? throw new IOException(UiText.Get("Layer did not return account information."));
        }
        lock (Profile.Sync)
        {
            var options = Profile.Layer;
            return new(options.Enabled, options.BindAddress, options.Port, options.GlobalPermissions,
                options.Accounts.Select(x => new PublicLayerAccount(x.Username, x.Enabled, x.Permissions)).ToArray());
        }
    }
    public async Task SaveLayerAccountAsync(bool upstream, string username, string password, bool enabled, LayerPermission? permissions)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64) throw new ArgumentException(UiText.Get("Username must contain 1–64 characters."));
        if (upstream)
        {
            if (connection is not LayerConnection layer || !IsConnected) throw new IOException(UiText.Get("Connect to the upstream Layer first."));
            await layer.RequestAsync(new() { Type = "layer.account.save", Username = username, Password = password,
                Layer = new(true, "", 0, 0, [new(username, enabled, permissions)]) });
            return;
        }
        var hash = password.Length > 0 ? PasswordHash.Create(password) : null;
        lock (Profile.Sync)
        {
            var account = Profile.Layer.Accounts.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (account is null)
            {
                if (hash is null) throw new ArgumentException(UiText.Get("New accounts require a password."));
                if (Profile.Layer.Accounts.Count >= 100) throw new ArgumentException(UiText.Get("At most 100 accounts."));
                account = new() { Username = username }; Profile.Layer.Accounts.Add(account);
            }
            if (hash is not null) account.PasswordHash = hash;
            account.Enabled = enabled; account.Permissions = permissions;
        }
        await RefreshLayerAccountsAsync();
    }
    public async Task DeleteLayerAccountAsync(bool upstream, string username)
    {
        if (upstream)
        {
            if (connection is not LayerConnection layer || !IsConnected) throw new IOException(UiText.Get("Connect to the upstream Layer first."));
            await layer.RequestAsync(new() { Type = "layer.account.delete", Username = username });
        }
        else { lock (Profile.Sync) Profile.Layer.Accounts.RemoveAll(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)); await RefreshLayerAccountsAsync(); }
    }
    public async Task SetGlobalPermissionsAsync(bool upstream, LayerPermission permissions)
    {
        if (upstream)
        {
            if (connection is not LayerConnection layer || !IsConnected) throw new IOException(UiText.Get("Connect to the upstream Layer first."));
            await layer.RequestAsync(new() { Type = "layer.global", Permissions = permissions });
        }
        else { lock (Profile.Sync) Profile.Layer.GlobalPermissions = permissions; await RefreshLayerAccountsAsync(); }
    }
    private void HandleEvent(string[] words)
    {
        try
        {
            if (words.Length == 0) return;
            string W(int i) => words.Length > i ? words[i] : "";
            lock (dataGate)
            {
                var aliveName = words[0] is "player.onSpawn" or "player.spawn" ? W(1) : words[0] == "player.onKill" ? W(2) : "";
                if (aliveName.Length > 0)
                {
                    var alive = words[0] != "player.onKill"; lifeStates[aliveName] = alive;
                    var affected = Players.FirstOrDefault(x => x.Name == aliveName); if (affected is not null) affected.IsAlive = alive;
                }
                if (words[0] == "player.onLeave") { lifeStates.Remove(W(1)); lifeQueries.Remove(W(1)); }
                if (words[0] is "server.onLevelLoaded" or "server.onRoundOver")
                {
                    lifeStates.Clear(); lifeQueries.Clear(); foreach (var player in Players) player.IsAlive = null;
                }
            }
            switch (words[0])
            {
                case "player.onChat" when !Profile.IsLayer:
                    var adminMessage = W(1) is "Server" or "Admin";
                    if (adminMessage)
                    {
                        lock (dataGate)
                        {
                            adminEchoes.RemoveAll(item => item.Sent < DateTimeOffset.Now.AddSeconds(-15));
                            var index = adminEchoes.FindIndex(item => item.Text == W(2));
                            if (index >= 0) { adminEchoes.RemoveAt(index); break; }
                        }
                    }
                    var player = PlayerSnapshot().FirstOrDefault(x => x.Name == W(1));
                    Journal.Add("Chat", W(1), player is null ? W(3) : $"Team {player.Team}", W(2), player?.Team ?? 0, game: Game, isAdminMessage: adminMessage, adminMessageType: adminMessage ? "Say" : "");
                    break;
                case "player.onJoin" when !Profile.IsLayer:
                    Journal.Add("Join", W(1), "", $"{W(1)} connected to the server"); break;
                case "player.onLeave":
                    lock (dataGate) Players.RemoveAll(x => x.Name == W(1));
                    if (!Profile.IsLayer) Journal.Add("Leave", W(1), "", $"{W(1)} disconnected from the server");
                    break;
                case "player.onKill" when !Profile.IsLayer:
                    var players = PlayerSnapshot();
                    Journal.Add("Kill", W(1), "", $"{W(1)} killed {W(2)} with {W(3)}",
                        players.FirstOrDefault(x => x.Name == W(1))?.Team ?? 0,
                        players.FirstOrDefault(x => x.Name == W(2))?.Team ?? 0, W(2), W(3), Game); break;
                case "punkBuster.onMessage":
                    Log("PunkBuster", W(1));
                    ParsePunkBuster(W(1));
                    break;
                default:
                    if (words[0] == "lightRcon.state") Log("Layer", W(1));
                    else Log("Event", string.Join(" | ", words));
                    break;
            }
            RemoteEvent?.Invoke(words);
            Plugins.Dispatch(words);
            Touch();
        }
        catch (Exception ex) { Log("Event error", ex.Message); }
    }
    private static readonly Regex PbPlayer = new(@"(?:PunkBuster Server:\s*)?(?<slot>\d+)\s+(?<guid>[0-9a-fA-F]{32})(?:\([^)]*\))?\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3}):\d+\s+.*?""(?<name>[^""]+)""", RegexOptions.Compiled);
    private void ParsePunkBuster(string message)
    {
        var match = PbPlayer.Match(message);
        if (!match.Success) return;
        lock (dataGate)
        {
            var player = Players.FirstOrDefault(x => x.Name == match.Groups["name"].Value);
            if (player is null) return;
            player.PbGuid = match.Groups["guid"].Value; player.Ip = match.Groups["ip"].Value; player.PbSlot = match.Groups["slot"].Value;
        }
    }
    public async ValueTask DisposeAsync()
    {
        store.LogsCleared -= Journal.ClearPersisted;
        await StopAsync();
        if (layerServer is not null) { await layerServer.DisposeAsync(); layerServer = null; }
        State = ConnectionState.Disposed;
        runCancellation?.Dispose();
    }
}
