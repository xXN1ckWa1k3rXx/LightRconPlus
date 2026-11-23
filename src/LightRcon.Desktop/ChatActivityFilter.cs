using LightRcon.Core;

namespace LightRcon.Desktop;

internal static class ChatActivityFilter
{
    public static Func<ActivityEntry, bool> Create(string query, PlayerInfo[] players)
    {
        query = query.Trim();
        if (query.Length == 0) return static _ => true;
        var names = players.Where(player => player.EaGuid.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            player.PbGuid.Equals(query, StringComparison.OrdinalIgnoreCase)).Select(player => player.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool MatchesName(string name) => name.Contains(query, StringComparison.OrdinalIgnoreCase) || names.Contains(name);
        // Resolve player GUIDs once per refresh instead of rebuilding the matching
        // name list for each of the journal's potentially 5,000 entries.
        return entry => MatchesName(entry.Name) || entry.TryGetKillDetails(out var victim, out _) && MatchesName(victim);
    }
}
