using System.Text.Json;

namespace LightRcon.Core;

public static class TeamCatalog
{
    private static readonly Dictionary<GameId, Dictionary<string, string[]>> definitions = Enum.GetValues<GameId>().ToDictionary(x => x,
        x => new Dictionary<string, string[]>(GameCatalog.Load<Dictionary<string, string[]>>(x, "teams"), StringComparer.OrdinalIgnoreCase));
    public static string Name(string map, string mode, int team, GameId game = GameId.BF4)
    {
        if (team == 0) return UiText.Get("Unassigned / Spectators");
        if (!definitions[game].TryGetValue(map + "|" + mode, out var teams) || team < 1 || team >= teams.Length)
            return UiText.Format("Team {0}", team);
        var definition = teams[team].Split(':');
        if (definition[0] is "cop" or "crim") return UiText.Get(definition[0] == "cop" ? "Cops" : "Criminals");
        string Faction(string code) => code switch { "us" => UiText.Get("US Army"), "ru" => UiText.Get("Russian Army"), "cn" => UiText.Get("Chinese Army"), _ => "" };
        if (definition[0] is "us" or "ru" or "cn") return Faction(definition[0]);
        if (definition[0] is "attackers" or "defenders")
        {
            var role = UiText.Get(definition[0] == "attackers" ? "Attackers" : "Defenders");
            var faction = Faction(definition.ElementAtOrDefault(1) ?? "");
            return faction.Length > 0 ? UiText.Format("{0} ({1})", role, faction) : role;
        }
        if (definition[0].StartsWith("squad", StringComparison.Ordinal) && int.TryParse(definition[0].AsSpan(5), out var squad)) return UiText.Format("Squad {0}", squad);
        return UiText.Format("Team {0}", team);
    }
}
