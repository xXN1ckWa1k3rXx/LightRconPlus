using System.Text.Json;

namespace LightRcon.Core;

public enum GameId { BF4, BF3, BFHL }

public static class GameCatalog
{
    public static GameId Parse(string code) => code.ToUpperInvariant() switch
    {
        "BF3" => GameId.BF3, "BF4" => GameId.BF4, "BFHL" => GameId.BFHL,
        _ => throw new RconException("Unsupported game. Expected BF3, BF4 or BFHL.")
    };
    public static string BattlelogCode(GameId game) => game switch
    {
        GameId.BF3 => "bf3", GameId.BF4 => "bf4", GameId.BFHL => "bfh",
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };
    internal static T Load<T>(GameId game, string resource)
    {
        var prefix = game.ToString().ToLowerInvariant() + "-";
        using var stream = typeof(GameCatalog).Assembly.GetManifestResourceStream("LightRcon.Core.Data." + prefix + resource + ".json");
        return JsonSerializer.Deserialize<T>(stream!, ProfileStore.Json)!;
    }
    public static Uri PlayerProfile(GameId game, string player, bool bf4db = false)
    {
        if (bf4db && game != GameId.BF4) throw new ArgumentException("BF4DB is available for BF4 only.");
        var name = Uri.EscapeDataString(player);
        return new(bf4db ? "https://bf4db.com/player/search?query=" + name
            : "https://battlelog.battlefield.com/" + BattlelogCode(game) + "/user/" + name);
    }
}
