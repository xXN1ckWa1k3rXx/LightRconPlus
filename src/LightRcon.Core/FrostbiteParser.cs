using System.Globalization;

namespace LightRcon.Core;

public static class FrostbiteParser
{
    private static int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static int? TeamScore(string text)
    {
        // BF4 also returns scores such as "798.000000". Match Procon's rounded
        // display; a missing/invalid value must not masquerade as zero tickets.
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)) return null;
        var rounded = decimal.Round(score, 0, MidpointRounding.ToEven);
        return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : null;
    }
    public static ServerInfo Server(string[] words)
    {
        if (words.Length < 10 || words[0] != "OK") throw new InvalidDataException("Invalid serverInfo response.");
        if (!int.TryParse(words[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var teams) || teams is < 0 or > 16 || words.Length < 10 + teams)
            throw new InvalidDataException("Invalid serverInfo team score block.");
        var uptimeIndex = 14 + teams;
        var uptime = words.Length > uptimeIndex && long.TryParse(words[uptimeIndex], out var value) ? value : 0;
        return new(words[1], Number(words[2]), Number(words[3]), words[4], words[5], Number(words[6]) + 1, Number(words[7]), uptime)
        { TeamScores = words.Skip(9).Take(teams).Select(TeamScore).ToArray(), TargetScore = TeamScore(words[9 + teams]) };
    }
    public static List<PlayerInfo> Players(string[] words, IReadOnlyList<PlayerInfo>? previous = null)
    {
        if (words.Length < 3) throw new InvalidDataException("Invalid player info block.");
        var fields = Number(words[1]);
        if (fields is < 1 or > 100 || words.Length < fields + 3) throw new InvalidDataException("Invalid player field count.");
        var count = Number(words[fields + 2]);
        if (count < 0 || count > 256 || words.Length < fields + 3 + count * fields) throw new InvalidDataException("Invalid player count.");
        List<PlayerInfo> result = [];
        for (var i = 0; i < count; i++)
        {
            var data = Enumerable.Range(0, fields).ToDictionary(j => words[j + 2], j => words[fields + 3 + i * fields + j]);
            string Get(string key, string fallback = "") => data.GetValueOrDefault(key, fallback);
            var name = Get("name");
            var old = previous?.FirstOrDefault(x => x.Name == name);
            result.Add(new()
            {
                Id = i + 1, Name = name, EaGuid = Get("guid"), Team = Number(Get("teamId")), Squad = Number(Get("squadId")),
                Kills = Number(Get("kills")), Deaths = Number(Get("deaths")), Score = Get("score", "—"), Rank = Get("rank", "—"), Ping = Get("ping", "—"),
                Tags = Get("clanTag", Get("tag", "—")), Kit = Get("kit", old?.Kit ?? "—"), Type = Get("type", "—"),
                PbGuid = Get("pbGuid", old?.PbGuid ?? ""), Ip = Get("ip", old?.Ip ?? ""), PbSlot = old?.PbSlot ?? "", FirstSeen = old?.FirstSeen ?? DateTimeOffset.Now
            });
        }
        return result;
    }
    internal static string[] WithPlayerAddresses(string[] words, IReadOnlyList<PlayerInfo> players)
    {
        _ = Players(words); // Validate the block before extending its schema for Layer clients.
        var fields = Number(words[1]); var count = Number(words[fields + 2]);
        var headers = words.Skip(2).Take(fields).ToArray();
        var extras = new[] { "ip", "pbGuid" }.Where(header => !headers.Contains(header)).ToArray();
        if (extras.Length == 0) return words;
        var nameIndex = Array.IndexOf(headers, "name");
        var result = new List<string> { words[0], (fields + extras.Length).ToString(CultureInfo.InvariantCulture) };
        result.AddRange(headers); result.AddRange(extras); result.Add(count.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < count; i++)
        {
            var start = fields + 3 + i * fields;
            var player = nameIndex < 0 ? null : players.FirstOrDefault(player => player.Name == words[start + nameIndex]);
            result.AddRange(words.Skip(start).Take(fields));
            foreach (var extra in extras) result.Add(extra == "ip" ? player?.Ip ?? "" : player?.PbGuid ?? "");
        }
        return result.ToArray();
    }
    public static List<MapEntry> Maps(string[] words)
    {
        if (words.Length < 3) throw new InvalidDataException("Invalid map list.");
        var count = Number(words[1]);
        var width = Number(words[2]);
        if (count < 0 || width < 3 || width > 32 || count > 1000 || words.Length < 3 + count * width) throw new InvalidDataException("Malformed map list.");
        return Enumerable.Range(0, count).Select(i => new MapEntry(words[3 + i * width], words[4 + i * width], Number(words[5 + i * width]))).ToList();
    }
    public static List<BanEntry> Bans(string[] words, int offset)
    {
        // BF4 sends six words per entry, with NO entry count prefix.
        if ((words.Length - 1) % 6 != 0) throw new InvalidDataException("Invalid ban list.");
        return Enumerable.Range(0, (words.Length - 1) / 6).Select(i =>
            new BanEntry(offset + i + 1, words[1 + i * 6], words[2 + i * 6], words[3 + i * 6], words[4 + i * 6], words[5 + i * 6], words[6 + i * 6])).ToList();
    }
}
