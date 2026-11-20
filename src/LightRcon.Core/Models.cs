using System.Globalization;
using System.Text.Json.Serialization;

namespace LightRcon.Core;

public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Server";
    public GameId Game { get; set; } = GameId.BF4;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 47200;
    public string Username { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
    public bool AutoLogin { get; set; }
    public List<string> EnabledPlugins { get; set; } = [];
    public List<string> InstalledPlugins { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> PluginSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DesiredSettings { get; set; } = [];
    public List<MapEntry>? DesiredMaps { get; set; }
    public Dictionary<string, string> DraftSettings { get; set; } = [];
    public ClientPreferences Client { get; set; } = new();
    public LayerOptions Layer { get; set; } = new();
    [JsonIgnore] public object Sync { get; } = new();
    [JsonIgnore] public bool IsLayer => !string.IsNullOrWhiteSpace(Username);
    [JsonIgnore] public string Endpoint => $"{(Host.Contains(':') ? $"[{Host}]" : Host)}:{Port}";
    [JsonIgnore] public string DisplayName => $"{Name}  ·  {Endpoint}";
    public override string ToString() => DisplayName;

    public static (string Host, int Port) ParseEndpoint(string text)
    {
        if (!Uri.TryCreate("tcp://" + text.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535 || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException(UiText.Get("Enter a valid IP/hostname:port. For IPv6 use [address]:port."));
        return (uri.Host.Trim('[', ']'), uri.Port);
    }
}

public sealed class ClientPreferences
{
    public string SelectedTab { get; set; } = "Players";
    public int PlayerTab { get; set; }
    public string ChatDraft { get; set; } = "";
    public string ChatAudience { get; set; } = "All Players";
    public string ChatDisplay { get; set; } = "Say";
    public int YellSeconds { get; set; } = 10;
    public bool ShowAdminSayYell { get; set; }
    public string BanSearch { get; set; } = "";
    public string ConsoleDraft { get; set; } = "";
    public Dictionary<string, List<GridColumnPreference>> Grids { get; set; } = [];
}

public sealed record GridColumnPreference(string Header, double Width, int Order, string? SortDirection);

[Flags]
public enum LayerPermission
{
    None = 0, View = 1, Chat = 2, Players = 4, Bans = 8, Maps = 16,
    Settings = 32, Console = 64, LayerAdmin = 128,
    All = View | Chat | Players | Bans | Maps | Settings | Console | LayerAdmin
}

public sealed class LayerOptions
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 47300;
    public LayerPermission GlobalPermissions { get; set; } = LayerPermission.View;
    public List<LayerAccount> Accounts { get; set; } = [];
}

public sealed class LayerAccount
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public LayerPermission? Permissions { get; set; }
    [JsonIgnore] public string PermissionSummary => Permissions?.ToString() ?? UiText.Get("Inherit global permissions");
}

public sealed record MapEntry(string FileName, string GameMode, int Rounds)
{
    public GameId Game { get; init; } = GameId.BF4;
    public string Name => MapCatalog.MapName(FileName, Game);
    public string ModeName => MapCatalog.ModeName(GameMode, Game);
    public string Detail => $"{ModeName} · {FileName}                     {UiText.Format("Round: {0}", Rounds)}";
}

public sealed class PlayerInfo
{
    public int Id { get; set; }
    public string Tags { get; set; } = "—";
    public string Name { get; set; } = "";
    public string EaGuid { get; set; } = "";
    public string PbGuid { get; set; } = "";
    public string Ip { get; set; } = "";
    [JsonIgnore] public string Geo => GeoLocation.CountryCode(Ip) is { Length: > 0 } code ? code : "—";
    [JsonIgnore] public string GeoDetails => GeoLocation.CountryName(Geo == "—" ? "" : Geo) + (Ip.Length > 0 ? " · " + Ip : "");
    public string PbSlot { get; set; } = "";
    public bool? IsAlive { get; set; }
    public int Team { get; set; }
    public int Squad { get; set; }
    public string Kit { get; set; } = "—";
    public string Type { get; set; } = "—";
    public string Rank { get; set; } = "—";
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public string Score { get; set; } = "—";
    [JsonIgnore] public int? TeamTickets { get; set; }
    [JsonIgnore] public string TeamName { get; set; } = "";
    [JsonIgnore] public string SquadName => Squad is > 0 and <= 26 ? new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu" }[Squad - 1] : "—";
    public string Ping { get; set; } = "—";
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.Now;
    public string Kdr => Deaths == 0 ? Kills.ToString(CultureInfo.InvariantCulture) : ((double)Kills / Deaths).ToString("0.00", CultureInfo.InvariantCulture);
    public string PlayTime => $"{(int)(DateTimeOffset.Now - FirstSeen).TotalHours:00}:{(DateTimeOffset.Now - FirstSeen):mm\\:ss}";
}

public sealed record BanEntry(int Index, string IdType, string Identifier, string BanType, string Seconds, string Rounds, string Reason)
{
    public string Name => IdType == "name" ? Identifier : "—";
    public string Ip => IdType == "ip" ? Identifier : "—";
    public string Guid => IdType == "guid" ? Identifier : "—";
    public string Remaining => BanType switch { "seconds" => Seconds + " " + UiText.Get("seconds"), "rounds" => Rounds + " " + UiText.Get("rounds"), _ => UiText.Get("perm") };
}

public sealed record ServerInfo(string Name, int Players, int MaxPlayers, string GameMode, string Map, int Round, int TotalRounds, long Uptime)
{
    public GameId Game { get; init; } = GameId.BF4;
    public int?[] TeamScores { get; init; } = [];
    public int? TargetScore { get; init; }
    public string MapName => MapCatalog.MapName(Map, Game);
    public string ModeName => MapCatalog.ModeName(GameMode, Game);
    public string UptimeText => TimeSpan.FromSeconds(Math.Max(0, Uptime)).ToString(@"d\.hh\:mm\:ss");
    public static ServerInfo Empty { get; } = new("Server", 0, 0, "—", "—", 0, 0, 0);
}

public sealed record ActivityEntry(long Sequence, DateTimeOffset Timestamp, string Kind, string Name, string Team, string Text)
{
    public GameId Game { get; init; } = GameId.BF4;
    public int ActorTeam { get; init; }
    public int TargetTeam { get; init; }
    public string TargetName { get; init; } = "";
    public string Weapon { get; init; } = "";
    public bool IsAdminMessage { get; init; }
    public bool IsPluginMessage { get; init; }
    public string AdminMessageType { get; init; } = "";
    public bool TryGetKillDetails(out string target, out string weapon)
    {
        target = TargetName; weapon = Weapon;
        if (Kind != "Kill") return false;
        if (target.Length > 0 && weapon.Length > 0) return true;
        // Older history may contain only the original English event text.
        var prefix = Name + " killed ";
        var split = Text.LastIndexOf(" with ", StringComparison.Ordinal);
        if (!Text.StartsWith(prefix, StringComparison.Ordinal) || split <= prefix.Length) return false;
        if (target.Length == 0) target = Text[prefix.Length..split];
        if (weapon.Length == 0) weapon = Text[(split + 6)..];
        return weapon.Length > 0;
    }
    [JsonIgnore] public string DisplayText => Kind == "Chat" ? GameText.Chat(Text)
        : TryGetKillDetails(out var target, out var weapon) ? $"{Name} {UiText.Get("killed")} {target} {UiText.Get("with")} {GameText.Weapon(weapon, Game)}" : Text;
    [JsonIgnore] public string Formatted
    {
        get
        {
            var local = Timestamp.LocalDateTime;
            var date = local.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern, CultureInfo.CurrentCulture);
            return Kind == "Chat"
                ? $"[{date} - {local:HH:mm:ss}] [{Name}] [{Team}] : {DisplayText}"
                : $"[{date}] {DisplayText}";
        }
    }
}

public sealed record ConsoleEntry(DateTimeOffset Timestamp, string Kind, string Text)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    [JsonIgnore] public bool IsPluginConsole => Kind is "Plugin" or "Procon" or "Lua" || Kind.StartsWith("Lua ", StringComparison.Ordinal);
    [JsonIgnore] public string Formatted => $"[{Timestamp.LocalDateTime:HH:mm:ss}] [{Kind}] {Text}";
}

public enum ConnectionState { Offline, Connecting, Authenticating, Synchronizing, Connected, RetryWaiting, Disposed }
