using System.ComponentModel;
using System.Windows.Media;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class ServerCard(ServerSession session) : INotifyPropertyChanged
{
    public ServerSession Session { get; } = session;
    public string Name => Session.Profile.Name;
    public override string ToString() => Name;
    public string Endpoint => Session.Profile.Endpoint;
    public string ServerName
    {
        get
        {
            if (!ReferenceEquals(Session.Info, ServerInfo.Empty) && !string.IsNullOrWhiteSpace(Session.Info.Name)) return Session.Info.Name;
            lock (Session.Profile.Sync)
                return Session.Profile.DesiredSettings.TryGetValue("vars.serverName", out var cached) && !string.IsNullOrWhiteSpace(cached) ? cached : Name;
        }
    }
    public string ServerDisplayName => (Session.Profile.IsLayer ? "[Layer] " : "") + ServerName;
    public string ConnectionLabel => Session.IsConnected ? $"[{Session.Info.Players}/{Session.Info.MaxPlayers}] {ServerDisplayName} [{Endpoint}]" : $"{UiText.Get("Offline")} — {ServerDisplayName} [{Endpoint}]";
    public string WelcomeTitle => $"{Name} | {ServerDisplayName} [{Endpoint}]";
    public string SidebarLabel => ServerDisplayName;
    public string Summary => Session.IsConnected ? UiText.Format("Players: {0}/{1}", Session.Info.Players, Session.Info.MaxPlayers) : ShortStatus;
    public string Rotation => Session.IsConnected ? $"{Session.Info.ModeName} — {Session.Info.MapName} [{Session.Info.Round}/{Session.Info.TotalRounds}]" : Session.LastError;
    public string Mode => Session.Profile.IsLayer ? "Layer / TLS" : UiText.Get("Direct RCON");
    public string ShortStatus => UiText.Get(Session.State switch
    {
        ConnectionState.Connected => "Connected", ConnectionState.Connecting => "Connecting", ConnectionState.Authenticating => "Authenticating",
        ConnectionState.Synchronizing => "Synchronizing", ConnectionState.RetryWaiting => UiText.Format("Retry in {0}s", Math.Max(0, (int)(Session.RetryAt.GetValueOrDefault() - DateTimeOffset.Now).TotalSeconds)), _ => "Offline"
    });
    public Brush StatusBrush => (Brush)(System.Windows.Application.Current?.TryFindResource(Session.IsConnected ? "Online" : "Offline") ?? Brushes.Gray);
    public string PlayerCount => Session.IsConnected ? $"{Session.Info.Players} / {Session.Info.MaxPlayers}" : "";
    public string Map => Session.IsConnected ? Session.Info.MapName + " · " + Session.Info.ModeName : UiText.Get("Waiting for server connection");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(null));
}
public sealed record MapChoice(string FileName, string Name, List<ModeChoice> Modes);
public sealed record MapGroup(string Name, List<MapChoice> Maps);
public sealed class ModeChoice(string fileName, string code, string name)
{
    public GameId Game { get; init; } = GameId.BF4;
    public string FileName { get; } = fileName;
    public string Code { get; } = code;
    public string Name { get; } = name;
    public int Rounds { get; set; } = 2;
    public int[] RoundChoices { get; } = Enumerable.Range(1, 20).ToArray();
    public MapEntry Entry => new(FileName, Code, Rounds) { Game = Game };
}
