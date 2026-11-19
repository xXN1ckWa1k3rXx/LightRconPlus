using System.Globalization;
using System.Text.Json;
using MoonSharp.Interpreter;

namespace LightRcon.Core.Plugins;

internal sealed class LuaPlugin : IPluginRuntime
{
    private readonly Script script = new(CoreModules.Preset_HardSandbox);
    private readonly ServerSession session;
    private readonly string path;
    private readonly string dataRoot;
    private readonly Dictionary<string, DynValue> handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginSetting> variables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Scheduled> timers = [];
    private readonly Dictionary<string, DynValue> modules = new(StringComparer.Ordinal);
    private readonly HashSet<string> loadingModules = new(StringComparer.Ordinal);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private int timerId;
    private bool enabled;
    private bool disposed;
    private sealed record Scheduled(DateTimeOffset Due, TimeSpan Interval, DynValue Callback);
    private static readonly Dictionary<string, string> Events = new()
    {
        ["player.onSpawn"] = "player_respawn", ["player.spawn"] = "player_respawn", ["player.onJoin"] = "player_join",
        ["player.onLeave"] = "player_leave", ["player.onChat"] = "player_chat", ["player.onKill"] = "player_kill",
        ["player.onAuthenticated"] = "player_authenticated", ["player.onDisconnect"] = "player_disconnect",
        ["player.onTeamChange"] = "player_team_change", ["player.onSquadChange"] = "player_squad_change",
        ["server.onRoundOver"] = "round_over", ["server.onLevelLoaded"] = "level_loaded",
        ["server.onLoadingLevel"] = "level_loading", ["server.onRoundOverPlayers"] = "round_over_players",
        ["server.onRoundOverTeamScores"] = "round_over_team_scores", ["punkBuster.onMessage"] = "punkbuster_message"
    };
    public LuaPlugin(string path, ServerSession session, string dataRoot)
    {
        this.path = path; this.session = session; this.dataRoot = dataRoot;
        Directory.CreateDirectory(dataRoot);
        script.Options.DebugPrint = message => session.Log("Lua", message);
        try
        {
            RegisterEvents();
            RegisterCommands();
            RegisterPlayers();
            RegisterServices();
            Call(script.LoadString(File.ReadAllText(path), codeFriendlyName: path));
            var version = script.Globals.Get("plugin").Table?.Get("api_version") ?? DynValue.Nil;
            if (!version.IsNil() && (version.Type != DataType.Number || version.Number != 1)) throw new InvalidDataException(UiText.Get("Unsupported Lua API version."));
        }
        catch { http.Dispose(); throw; }
    }
    public PluginDetails Details
    {
        get
        {
            var metadata = script.Globals.Get("plugin").Table;
            string Value(string key) => metadata?.Get(key).Type == DataType.String ? metadata.Get(key).String : "";
            return new(path, Value("name") is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(path), Value("version"), Value("author"), Value("website"), Value("description"), "Lua");
        }
    }
    private Table Namespace(string name)
    {
        var table = new Table(script);
        script.Globals[name] = table;
        return table;
    }
    private static string Text(DynValue value) => value.Type switch
    {
        DataType.String => value.String,
        DataType.Number => value.Number.ToString(CultureInfo.InvariantCulture),
        DataType.Boolean => value.Boolean ? "true" : "false",
        _ => throw new ScriptRuntimeException("Expected a string, number or boolean.")
    };
    private static string Arg(CallbackArguments args, int index) => Text(args[index]);
    private static double Number(CallbackArguments args, int index) => args.AsType(index, "argument", DataType.Number, false).Number;
    private static DynValue Function(CallbackArguments args, int index)
    {
        var callback = args[index];
        if (callback.Type is not (DataType.Function or DataType.ClrFunction)) throw new ScriptRuntimeException("Expected a function.");
        return callback;
    }
    private static void Bind(Table table, string name, Func<CallbackArguments, DynValue> callback) => table[name] = DynValue.NewCallback((_, args) => callback(args));
    private DynValue Array(IEnumerable<DynValue> values) => DynValue.NewTable(new Table(script, values.ToArray()));
    private DynValue Send(params string[] words) => Array(session.ExecuteAsync(words).GetAwaiter().GetResult().Select(DynValue.NewString));
    private void RegisterEvents()
    {
        var events = Namespace("event");
        foreach (var name in Events.Values.Concat(["raw", "plugin_enable", "plugin_disable", "setting_changed"]).Distinct())
        {
            var slot = new Table(script);
            events[name] = slot;
            Bind(slot, "set", args => {
                var callback = args[args[0].Type == DataType.Table ? 1 : 0];
                if (callback.IsNil()) handlers.Remove(name);
                else if (callback.Type is DataType.Function or DataType.ClrFunction) handlers[name] = callback;
                else throw new ScriptRuntimeException("Expected a function or nil.");
                return DynValue.Nil;
            });
        }
        Bind(events, "on", args => { handlers[Arg(args, 0)] = Function(args, 1); return DynValue.Nil; });
        Bind(events, "off", args => { handlers.Remove(Arg(args, 0)); return DynValue.Nil; });
    }
    private string[] Scope(CallbackArguments args, int index, int target)
    {
        var scope = index >= args.Count || args[index].IsNil() ? "all" : Arg(args, index);
        return scope switch
        {
            "all" => ["all"],
            "player" => ["player", Arg(args, target)],
            "team" => ["team", Arg(args, target)],
            "squad" => ["squad", Arg(args, target), Arg(args, target + 1)],
            _ => throw new ScriptRuntimeException("Expected all, player, team or squad.")
        };
    }
    private void RegisterCommands()
    {
        var rcon = Namespace("rcon");
        Bind(rcon, "command", args => Send(Enumerable.Range(0, args.Count).Select(index => Arg(args, index)).ToArray()));
        var admin = Namespace("admin");
        Bind(admin, "say", args => Send(["admin.say", Arg(args, 1), .. Scope(args, 0, 2)]));
        Bind(admin, "yell", args => Send(["admin.yell", Arg(args, 1), Arg(args, 2), .. Scope(args, 0, 3)]));
        Bind(admin, "kick", args => Send("admin.kickPlayer", Arg(args, 0), args.Count > 1 ? Arg(args, 1) : ""));
        Bind(admin, "kill", args => Send("admin.killPlayer", Arg(args, 0)));
        Bind(admin, "move", args => Send("admin.movePlayer", Arg(args, 0), Arg(args, 1), Arg(args, 2), args.Count > 3 ? Arg(args, 3) : "false"));
        var server = Namespace("server");
        Bind(server, "get_info", _ => ServerInfo());
        Bind(server, "get_settings", _ => ObjectTable(session.SettingsSnapshot()));
        Bind(server, "is_connected", _ => DynValue.NewBoolean(session.IsConnected));
        Bind(server, "get_game", _ => DynValue.NewString(session.Game.ToString()));
        Bind(server, "restart_round", _ => Send("mapList.restartRound"));
        Bind(server, "next_round", _ => Send("mapList.runNextRound"));
        var maps = Namespace("maps");
        Bind(maps, "list", _ => Array(session.MapSnapshot().Select(map => ObjectTable(new Dictionary<string, object?> { ["name"] = map.FileName, ["mode"] = map.GameMode, ["rounds"] = map.Rounds }))));
        Bind(maps, "add", args => Send("mapList.add", Arg(args, 0), Arg(args, 1), Arg(args, 2)));
        Bind(maps, "remove", args => Send("mapList.remove", Arg(args, 0)));
        Bind(maps, "clear", _ => Send("mapList.clear"));
        Bind(maps, "save", _ => Send("mapList.save"));
        Bind(maps, "load", _ => Send("mapList.load"));
        Bind(maps, "set_next", args => Send("mapList.setNextMapIndex", Arg(args, 0)));
        var bans = Namespace("bans");
        Bind(bans, "list", _ => Array(session.BanSnapshot().Select(ban => ObjectTable(new Dictionary<string, object?> { ["index"] = ban.Index, ["id_type"] = ban.IdType, ["identifier"] = ban.Identifier, ["duration"] = ban.BanType, ["seconds"] = ban.Seconds, ["rounds"] = ban.Rounds, ["reason"] = ban.Reason }))));
        Bind(bans, "add", args => Send(["banList.add", .. Enumerable.Range(0, args.Count).Select(index => Arg(args, index))]));
        foreach (var operation in new[] { "remove", "clear", "save", "load" })
            Bind(bans, operation, args => Send(["banList." + operation, .. Enumerable.Range(0, args.Count).Select(index => Arg(args, index))]));
        foreach (var pair in new[] { ("reserved_slots", "reservedSlotsList"), ("spectators", "spectatorList") })
        {
            var table = Namespace(pair.Item1);
            Bind(table, "list", _ => Array((pair.Item1 == "spectators" ? session.SpectatorSnapshot() : session.ReservedSnapshot()).Select(DynValue.NewString)));
            foreach (var operation in new[] { "add", "remove", "clear", "save", "load" })
                Bind(table, operation, args => Send([pair.Item2 + "." + operation, .. Enumerable.Range(0, args.Count).Select(index => Arg(args, index))]));
        }
        foreach (var group in SettingsCatalog.For(session.Game).GroupBy(setting => setting.Command.Split('.')[0]))
        {
            var table = Namespace(group.Key);
            foreach (var setting in group)
            {
                var command = setting.Command;
                Bind(table, command[(command.IndexOf('.') + 1)..], args => Send([command, .. Enumerable.Range(0, args.Count).Select(index => Arg(args, index))]));
            }
        }
        foreach (var group in new Dictionary<string, string[]> {
            ["mapList"] = ["list", "availableMaps", "getMapIndices", "getRounds", "add", "remove", "clear", "save", "load", "setNextMapIndex", "restartRound", "runNextRound", "endRound"],
            ["banList"] = ["list", "add", "remove", "clear", "save", "load"],
            ["reservedSlotsList"] = ["list", "add", "remove", "clear", "save", "aggressiveJoin"],
            ["spectatorList"] = ["list", "add", "remove", "clear", "save", "load"],
            ["punkBuster"] = ["isActive", "pb_sv_command"], ["fairFight"] = ["isActive"], ["player"] = ["isAlive"] })
        {
            var table = Namespace(group.Key);
            foreach (var method in group.Value) Bind(table, method, args => Send([group.Key + "." + method, .. Enumerable.Range(0, args.Count).Select(index => Arg(args, index))]));
        }
        foreach (var method in new[] { "listPlayers", "kickPlayer", "killPlayer", "movePlayer" })
            Bind(admin, method, args => Send(["admin." + method, .. Enumerable.Range(0, args.Count).Select(index => Arg(args, index))]));
        var procon = Namespace("procon");
        Bind(procon, "execute_command", args => {
            var words = Enumerable.Range(0, args.Count).Select(index => Arg(args, index)).ToArray();
            if (words[0] == "procon.protected.send") return Send(words[1..]);
            if (words[0] == "procon.protected.pluginconsole.write") { session.Log("Lua", string.Join(" ", words.Skip(1))); return DynValue.Nil; }
            return FromJson(session.Plugins.ProconCommand(words).GetAwaiter().GetResult());
        });
        Bind(procon, "call", args => FromJson(session.Plugins.ProconCall(Arg(args, 0), Arg(args, 1), Enumerable.Range(2, args.Count - 2).Select(index => ToObject(args[index])!).ToArray()).GetAwaiter().GetResult()));
    }
    private void RegisterPlayers()
    {
        var players = Namespace("players");
        Bind(players, "get", args => session.PlayerSnapshot().Any(player => player.Name == Arg(args, 0)) ? Player(Arg(args, 0)) : DynValue.Nil);
        Bind(players, "list", _ => Array(session.PlayerSnapshot().Select(player => Player(player.Name))));
        Bind(players, "count", _ => DynValue.NewNumber(session.PlayerSnapshot().Length));
    }
    private DynValue Player(string name)
    {
        var table = new Table(script);
        PlayerInfo? Current() => session.PlayerSnapshot().FirstOrDefault(player => player.Name == name);
        Bind(table, "get_name", _ => DynValue.NewString(name));
        Bind(table, "get_guid", _ => DynValue.NewString(Current()?.EaGuid ?? ""));
        Bind(table, "get_pb_guid", _ => DynValue.NewString(Current()?.PbGuid ?? ""));
        Bind(table, "get_ip", _ => DynValue.NewString(Current()?.Ip ?? ""));
        Bind(table, "get_team", _ => DynValue.NewNumber(Current()?.Team ?? 0));
        Bind(table, "get_squad", _ => DynValue.NewNumber(Current()?.Squad ?? 0));
        Bind(table, "get_kills", _ => DynValue.NewNumber(Current()?.Kills ?? 0));
        Bind(table, "get_deaths", _ => DynValue.NewNumber(Current()?.Deaths ?? 0));
        Bind(table, "get_ping", _ => int.TryParse(Current()?.Ping, out var ping) ? DynValue.NewNumber(ping) : DynValue.Nil);
        Bind(table, "is_alive", _ => Current()?.IsAlive is { } alive ? DynValue.NewBoolean(alive) : DynValue.Nil);
        Bind(table, "kick", args => Send("admin.kickPlayer", name, args.Count > 0 ? Arg(args, 0) : ""));
        Bind(table, "kill", _ => Send("admin.killPlayer", name));
        Bind(table, "move", args => Send("admin.movePlayer", name, Arg(args, 0), Arg(args, 1), args.Count > 2 ? Arg(args, 2) : "false"));
        Bind(table, "say", args => Send("admin.say", Arg(args, 0), "player", name));
        Bind(table, "yell", args => Send("admin.yell", Arg(args, 0), Arg(args, 1), "player", name));
        return DynValue.NewTable(table);
    }
    private DynValue ServerInfo() => ObjectTable(new Dictionary<string, object?>
    {
        ["name"] = session.Info.Name, ["map"] = session.Info.Map, ["mode"] = session.Info.GameMode,
        ["players"] = session.Info.Players, ["max_players"] = session.Info.MaxPlayers, ["round"] = session.Info.Round,
        ["total_rounds"] = session.Info.TotalRounds, ["uptime"] = session.Info.Uptime, ["game"] = session.Game.ToString(),
        ["host"] = session.Profile.Host, ["port"] = session.Profile.Port
    });
    private void RegisterServices()
    {
        var plugin = Namespace("plugin"); plugin["api_version"] = 1;
        var log = Namespace("log");
        foreach (var level in new[] { "info", "warning", "error", "debug" }) Bind(log, level, args => { session.Log("Lua " + level, Arg(args, 0)); return DynValue.Nil; });
        var settings = Namespace("settings");
        Bind(settings, "define", args => {
            var name = Arg(args, 0);
            var type = Arg(args, 1);
            var value = type.Equals("stringarray", StringComparison.OrdinalIgnoreCase) && args[2].Type == DataType.Table
                ? string.Join('|', Enumerable.Range(1, args[2].Table.Length).Select(index => System.Net.WebUtility.UrlEncode(Text(args[2].Table.Get(index)))))
                : Arg(args, 2);
            var readOnly = args.Count > 3 && args[3].Type == DataType.Boolean && args[3].Boolean;
            var setting = new PluginSetting(name, type, value, readOnly);
            PluginManager.Validate(setting, value);
            if (!variables.TryAdd(name, setting)) throw new ScriptRuntimeException("Duplicate setting: " + name);
            return DynValue.Nil;
        });
        Bind(settings, "get", args => {
            if (!variables.TryGetValue(Arg(args, 0), out var setting)) return DynValue.Nil;
            return setting.Type.ToLowerInvariant() switch {
                "bool" or "boolean" => DynValue.NewBoolean(bool.Parse(setting.Value)),
                "int" or "int32" or "float" or "double" or "single" => DynValue.NewNumber(double.Parse(setting.Value, CultureInfo.InvariantCulture)),
                "stringarray" => Array(setting.Value.Split('|').Select(item => DynValue.NewString(System.Net.WebUtility.UrlDecode(item)))),
                _ => DynValue.NewString(setting.Value) };
        });
        var storage = Namespace("storage");
        Bind(storage, "get", args => {
            var file = DataPath(Arg(args, 0));
            if (!File.Exists(file)) return args.Count > 1 ? args[1] : DynValue.Nil;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return FromJson(document.RootElement);
        });
        Bind(storage, "set", args => { ProfileStore.AtomicWrite(DataPath(Arg(args, 0)), JsonSerializer.Serialize(ToObject(args[1]))); return DynValue.Nil; });
        Bind(storage, "remove", args => { File.Delete(DataPath(Arg(args, 0))); return DynValue.Nil; });
        var timer = Namespace("timer");
        foreach (var repeat in new[] { false, true })
            Bind(timer, repeat ? "every" : "after", args => {
                var seconds = Number(args, 0);
                if (!double.IsFinite(seconds) || seconds < 0.1 || seconds > 86400 * 365 || timers.Count >= 128) throw new ScriptRuntimeException("Invalid timer interval or timer limit exceeded.");
                var id = ++timerId;
                timers[id] = new(DateTimeOffset.UtcNow.AddSeconds(seconds), repeat ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero, Function(args, 1));
                return DynValue.NewNumber(id);
            });
        Bind(timer, "cancel", args => DynValue.NewBoolean(timers.Remove((int)Number(args, 0))));
        var json = Namespace("json");
        Bind(json, "encode", args => DynValue.NewString(JsonSerializer.Serialize(ToObject(args[0]))));
        Bind(json, "decode", args => { using var document = JsonDocument.Parse(Arg(args, 0)); return FromJson(document.RootElement); });
        var network = Namespace("http");
        Bind(network, "request", args => {
            if (!Uri.TryCreate(Arg(args, 1), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ScriptRuntimeException("Expected an HTTP or HTTPS URL.");
            using var request = new HttpRequestMessage(new HttpMethod(Arg(args, 0)), uri);
            if (args.Count > 2 && !args[2].IsNil()) request.Content = new StringContent(Arg(args, 2));
            if (args.Count > 3 && args[3].Type == DataType.Table)
                foreach (var header in args[3].Table.Pairs)
                {
                    var key = Text(header.Key);
                    var value = Text(header.Value);
                    if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
                    {
                        request.Content.Headers.Remove(key);
                        request.Content.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);
            using var stream = response.Content.ReadAsStream();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = stream.ReadAsync(buffer, cancellation.Token).AsTask().GetAwaiter().GetResult();
                if (count == 0) break;
                if (body.Length + count > 1_048_576) throw new ScriptRuntimeException("HTTP response exceeds 1 MiB.");
                body.Write(buffer, 0, count);
            }
            return ObjectTable(new Dictionary<string, object?> { ["status"] = (int)response.StatusCode, ["body"] = System.Text.Encoding.UTF8.GetString(body.ToArray()), ["success"] = response.IsSuccessStatusCode });
        });
        script.Globals["require"] = DynValue.NewCallback((_, args) => {
            var name = Arg(args, 0);
            if (name == "lightrcon") return DynValue.NewTable(script.Globals);
            var value = script.Globals.Get(name);
            if (value.Type == DataType.Table) return value;
            if (modules.TryGetValue(name, out var module)) return module;
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var source = Path.GetFullPath(Path.Combine(directory, name.Replace('.', Path.DirectorySeparatorChar) + ".lua"));
            if (!source.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(source)) throw new ScriptRuntimeException("Unknown module: " + name);
            if (!loadingModules.Add(name)) throw new ScriptRuntimeException("Circular module dependency: " + name);
            try
            {
                module = Call(script.LoadString(File.ReadAllText(source), codeFriendlyName: source));
                if (module.IsNil()) module = DynValue.True;
                modules[name] = module;
                return module;
            }
            finally { loadingModules.Remove(name); }
        });
    }
    private string DataPath(string key)
    {
        if (key.Length == 0 || key.Length > 100) throw new ScriptRuntimeException("Storage key must contain 1–100 characters.");
        return Path.Combine(dataRoot, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + ".json");
    }
    private DynValue ObjectTable<T>(IEnumerable<KeyValuePair<string, T>> values)
    {
        var table = new Table(script);
        foreach (var pair in values) table[pair.Key] = FromJson(JsonSerializer.SerializeToElement(pair.Value));
        return DynValue.NewTable(table);
    }
    private DynValue FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => DynValue.Nil,
        JsonValueKind.True => DynValue.True, JsonValueKind.False => DynValue.False,
        JsonValueKind.Number => DynValue.NewNumber(value.GetDouble()),
        JsonValueKind.String => DynValue.NewString(value.GetString()!),
        JsonValueKind.Array => Array(value.EnumerateArray().Select(FromJson)),
        JsonValueKind.Object => JsonObject(value),
        _ => DynValue.Nil
    };
    private DynValue JsonObject(JsonElement value)
    {
        var table = new Table(script);
        foreach (var pair in value.EnumerateObject()) table[pair.Name] = FromJson(pair.Value);
        return DynValue.NewTable(table);
    }
    private static object? ToObject(DynValue value, int depth = 0)
    {
        if (depth > 32) throw new ScriptRuntimeException("JSON nesting limit exceeded.");
        return value.Type switch
        {
            DataType.Nil or DataType.Void => null, DataType.String => value.String, DataType.Number => value.Number, DataType.Boolean => value.Boolean,
            DataType.Table when value.Table.Pairs.All(pair => pair.Key.Type == DataType.Number && pair.Key.Number >= 1 && pair.Key.Number <= value.Table.Length && pair.Key.Number % 1 == 0) => Enumerable.Range(1, value.Table.Length).Select(index => ToObject(value.Table.Get(index), depth + 1)).ToArray(),
            DataType.Table => value.Table.Pairs.ToDictionary(pair => Text(pair.Key), pair => ToObject(pair.Value, depth + 1)),
            _ => throw new ScriptRuntimeException("JSON supports only primitive values and tables.")
        };
    }
    private DynValue Call(DynValue function, params DynValue[] args)
    {
        if (function.Type == DataType.ClrFunction) return script.Call(function, args);
        var coroutine = script.CreateCoroutine(function).Coroutine;
        coroutine.AutoYieldCounter = 100000;
        var result = coroutine.Resume(args);
        if (coroutine.State != CoroutineState.Dead) throw new ScriptRuntimeException("Lua callback exceeded its instruction budget or yielded.");
        return result;
    }
    private void Emit(string name, DynValue data) { if (handlers.TryGetValue(name, out var callback)) Call(callback, data); }
    public void Enable()
    {
        if (enabled) return;
        enabled = true;
        try { Emit("plugin_enable", ServerInfo()); }
        catch { enabled = false; timers.Clear(); throw; }
    }
    public IReadOnlyList<PluginSetting> GetSettings() => variables.Values.ToArray();
    public void SetSetting(string name, string value)
    {
        if (!variables.TryGetValue(name, out var variable)) throw new InvalidDataException(UiText.Get("Unknown plugin setting."));
        PluginManager.Validate(variable, value);
        variables[name] = variable with { Value = value };
        if (enabled) Emit("setting_changed", ObjectTable(new Dictionary<string, object?> { ["name"] = name, ["value"] = value }));
    }
    public void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in timers.Where(pair => pair.Value.Due <= now).ToArray())
        {
            if (pair.Value.Interval == TimeSpan.Zero) timers.Remove(pair.Key);
            else timers[pair.Key] = pair.Value with { Due = now + pair.Value.Interval };
            try { Call(pair.Value.Callback); }
            catch { timers.Remove(pair.Key); throw; }
        }
    }
    public void Handle(string[] words)
    {
        if (words.Length == 0) return;
        var player = Player(words.ElementAtOrDefault(1) ?? "").Table;
        player["words"] = Array(words.Select(DynValue.NewString));
        player["name"] = words.ElementAtOrDefault(1) ?? "";
        player["event"] = words[0];
        player["player"] = Player(words.ElementAtOrDefault(1) ?? "");
        if (words[0] == "player.onKill")
        {
            player["killer"] = Player(words.ElementAtOrDefault(1) ?? "");
            player["victim"] = Player(words.ElementAtOrDefault(2) ?? "");
            player["weapon"] = words.ElementAtOrDefault(3) ?? "";
            player["headshot"] = words.ElementAtOrDefault(4)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        }
        if (words[0] == "player.onChat") { player["message"] = words.ElementAtOrDefault(2) ?? ""; player["scope"] = words.ElementAtOrDefault(3) ?? ""; }
        var data = DynValue.NewTable(player);
        if (Events.TryGetValue(words[0], out var name)) Emit(name, data);
        Emit(words[0], data); Emit("raw", data);
    }
    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        try { if (enabled) Emit("plugin_disable", ServerInfo()); }
        finally { enabled = false; timers.Clear(); handlers.Clear(); http.Dispose(); }
        return ValueTask.CompletedTask;
    }
}
