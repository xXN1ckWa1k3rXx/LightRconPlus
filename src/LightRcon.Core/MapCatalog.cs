using System.Text.Json;

namespace LightRcon.Core;

public sealed record AvailableMap(string FileName, string Name, List<AvailableMode> Modes);
public sealed record AvailableMode(string Code, string Name);

public static class MapCatalog
{
    private static readonly Dictionary<GameId, List<AvailableMap>> games = Enum.GetValues<GameId>().ToDictionary(x => x, x => GameCatalog.Load<List<AvailableMap>>(x, "maps"));
    public static IReadOnlyList<AvailableMap> All => For(GameId.BF4);
    public static IReadOnlyList<AvailableMap> For(GameId game) => games[game];
    public static string MapName(string code, GameId game = GameId.BF4) => For(game).FirstOrDefault(x => x.FileName.Equals(code, StringComparison.OrdinalIgnoreCase))?.Name ?? code;
    public static string ModeName(string code, GameId game = GameId.BF4) => For(game).SelectMany(x => x.Modes).FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Name ?? code;
    public static string Expansion(string code, GameId game = GameId.BF4) => game != GameId.BF4 ? "Maps" : code switch
    {
        var x when x.StartsWith("MP_", StringComparison.Ordinal) => "Base Game",
        var x when x.StartsWith("XP0_", StringComparison.Ordinal) => "Second Assault",
        var x when x.StartsWith("XP1_", StringComparison.Ordinal) => "China Rising",
        var x when x.StartsWith("XP2_", StringComparison.Ordinal) => "Naval Strike",
        var x when x.StartsWith("XP3_", StringComparison.Ordinal) => "Dragon's Teeth",
        var x when x.StartsWith("XP4_", StringComparison.Ordinal) => "Final Stand",
        _ => "Additional Maps"
    };
    public static int ExpansionOrder(string group) => Array.IndexOf(new[] { "Base Game", "China Rising", "Second Assault", "Naval Strike", "Dragon's Teeth", "Final Stand", "Additional Maps" }, group);
}
