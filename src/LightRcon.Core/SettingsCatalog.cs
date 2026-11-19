using System.Text;

namespace LightRcon.Core;

[Flags]
public enum SettingGames { BF3 = 1, BF4 = 2, BFH = 4, All = BF3 | BF4 | BFH }
public sealed record SettingDefinition(string Command, string Label, string Group, string Kind = "text", string Hint = "", SettingGames Games = SettingGames.All);
public sealed record SettingChoice(string Value, string Label);

public static class SettingsCatalog
{
    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        new("vars.serverName", "Server name", "Server Information"),
        new("vars.serverDescription", "Description", "Server Information", "multiline"),
        new("vars.serverMessage", "Join Message", "Server Information", "multiline"),
        new("vars.bannerUrl", "Banner URL", "Server Information", Games: SettingGames.BF3),
        new("vars.serverType", "Server type", "Server Information", "text", "OFFICIAL / RANKED / UNRANKED / PRIVATE; some settings can only be changed at startup", SettingGames.BF4 | SettingGames.BFH),
        new("vars.maxPlayers", "Maximum players", "Slots & Teams", "number"),
        new("vars.maxSpectators", "Maximum spectators", "Slots & Teams", "number", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.commander", "Commander", "Slots & Teams", "bool", Games: SettingGames.BF4),
        new("vars.hacker", "Hacker", "Slots & Teams", "bool", Games: SettingGames.BFH),
        new("vars.alwaysAllowSpectators", "Always allow spectators", "Slots & Teams", "bool", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.autoBalance", "Auto balance", "Slots & Teams", "bool"),
        new("reservedSlotsList.aggressiveJoin", "Reserved slots aggressive join", "Slots & Teams", "bool"),
        new("vars.preset", "Game preset (server side)", "Gameplay", "preset", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.friendlyFire", "Friendly fire", "Gameplay", "bool"),
        new("vars.regenerateHealth", "Regenerate health", "Gameplay", "bool"),
        new("vars.soldierHealth", "Soldier health (%)", "Gameplay", "number"),
        new("vars.bulletDamage", "Bullet damage (%)", "Gameplay", "number"),
        new("vars.playerRespawnTime", "Player respawn time (%)", "Gameplay", "number"),
        new("vars.playerManDownTime", "Player man-down time (%)", "Gameplay", "number", Games: SettingGames.BF3 | SettingGames.BFH),
        new("vars.onlySquadLeaderSpawn", "Only squad leader spawn", "Gameplay", "bool"),
        new("vars.forceReloadWholeMags", "Force reload whole magazines", "Gameplay", "bool", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.unlockMode", "Unlock mode", "Gameplay", "choice"),
        new("vars.gunMasterWeaponsPreset", "Gun Master weapons preset", "Gameplay", "choice", Games: SettingGames.BF3 | SettingGames.BF4),
        new("vars.vehicleSpawnAllowed", "Vehicle spawn allowed", "Vehicles", "bool"),
        new("vars.vehicleSpawnDelay", "Vehicle spawn delay (%)", "Vehicles", "number"),
        new("vars.3pCam", "Third-person vehicle camera", "Vehicles", "bool"),
        new("vars.hud", "HUD", "HUD & Visibility", "bool"),
        new("vars.miniMap", "Minimap", "HUD & Visibility", "bool"),
        new("vars.miniMapSpotting", "Minimap spotting", "HUD & Visibility", "bool"),
        new("vars.3dSpotting", "3D spotting", "HUD & Visibility", "bool"),
        new("vars.nameTag", "Name tags", "HUD & Visibility", "bool"),
        new("vars.killCam", "Kill camera", "HUD & Visibility", "bool"),
        new("vars.crossHair", "Crosshair", "HUD & Visibility", "bool"),
        new("vars.hitIndicatorsEnabled", "Hit indicators", "HUD & Visibility", "bool", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.gameModeCounter", "Game mode counter (%)", "Rounds", "number"),
        new("vars.ctfRoundTimeModifier", "CTF round time modifier (%)", "Rounds", "number", Games: SettingGames.BF3),
        new("vars.roundTimeLimit", "Round time limit (%)", "Rounds", "number", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.ticketBleedRate", "Ticket bleed rate (%)", "Rounds", "number", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.roundStartPlayerCount", "Round start player count", "Rounds", "number"),
        new("vars.roundRestartPlayerCount", "Round restart player count", "Rounds", "number"),
        new("vars.roundLockdownCountdown", "Round lockdown countdown", "Rounds", "number"),
        new("vars.roundWarmupTimeout", "Round warmup timeout", "Rounds", "number"),
        new("vars.idleTimeout", "Idle timeout (seconds)", "Moderation", "number"),
        new("vars.idleBanRounds", "Idle ban rounds", "Moderation", "number"),
        new("vars.teamKillCountForKick", "Team kills before kick", "Moderation", "number"),
        new("vars.teamKillKickForBan", "Team-kill kicks before ban", "Moderation", "number"),
        new("vars.teamKillValueForKick", "Team-kill value before kick", "Moderation", "number"),
        new("vars.teamKillValueIncrease", "Team-kill value increase", "Moderation", "number"),
        new("vars.teamKillValueDecreasePerSecond", "Team-kill value decrease per second", "Moderation", "number"),
        new("vars.rankLimit", "Rank limit", "Miscellaneous", "number", Games: SettingGames.BF3),
        new("vars.premiumStatus", "Premium priority", "Miscellaneous", "bool", Games: SettingGames.BF3),
        new("vars.mpExperience", "MP experience", "Miscellaneous", Games: SettingGames.BF4 | SettingGames.BFH),
        new("vars.roundPlayersReadyBypassTimer", "Ready bypass timer", "Miscellaneous", "number", Games: SettingGames.BF4),
        new("vars.roundPlayersReadyMinCount", "Minimum ready players", "Miscellaneous", "number", Games: SettingGames.BF4),
        new("vars.roundPlayersReadyPercent", "Ready players (%)", "Miscellaneous", "number", Games: SettingGames.BF4),
        new("vars.roundStartReadyPlayersNeeded", "Ready players needed", "Miscellaneous", "number", Games: SettingGames.BFH)
    ];
    private static SettingGames Mask(GameId game) => game switch { GameId.BF3 => SettingGames.BF3, GameId.BF4 => SettingGames.BF4, GameId.BFHL => SettingGames.BFH, _ => 0 };
    public static bool Supports(string command, GameId game) => Find(command) is { } definition && (definition.Games & Mask(game)) != 0;
    public static IReadOnlyList<SettingDefinition> For(GameId game) => All.Where(definition => (definition.Games & Mask(game)) != 0).ToArray();
    public static IReadOnlyList<SettingChoice> Choices(string command, GameId game = GameId.BF4) => command switch
    {
        "vars.gunMasterWeaponsPreset" when game == GameId.BF3 => [new("0", "Standard"), new("1", "Reversed"), new("2", "Lightweight"), new("3", "Heavy gear"), new("4", "Pistol run"), new("5", "Sniper heaven"), new("6", "US arms race"), new("7", "RU arms race"), new("8", "EU arms race")],
        "vars.preset" => [new("NORMAL", "Normal"), new("CLASSIC", "Classic"), new("HARDCORE", "Hardcore"), new("INFANTRY", "Infantry"), new("CUSTOM", "Custom")],
        "vars.gunMasterWeaponsPreset" => [new("0", "Standard"), new("1", "Classic"), new("2", "Pistols"), new("3", "DLC Weapons"), new("4", "Troll")],
        "vars.unlockMode" => [new("none", "None"), new("all", "All"), new("common", "Common"), new("stats", "Based on stats")],
        _ => []
    };
    public static (string Preset, bool Locked) ParsePreset(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts.ElementAtOrDefault(0) ?? "", parts.Length > 1 && bool.TryParse(parts[1], out var locked) && locked);
    }
    public static string[] SetCommand(string command, string value)
    {
        if (command != "vars.preset") return [command, value];
        var (preset, locked) = ParsePreset(value);
        return [command, preset, locked ? "true" : "false"];
    }
    public static SettingDefinition? Find(string command) => All.FirstOrDefault(x => x.Command == command);
    public static void Validate(string command, string value, GameId game = GameId.BF4)
    {
        var definition = Find(command) ?? throw new ArgumentException(UiText.Get("Unknown setting."));
        if (!Supports(command, game)) throw new ArgumentException(UiText.Format("Setting {0} is not available for {1}.", command, game));
        if (value.Contains('\0')) throw new ArgumentException(UiText.Get("Settings cannot contain NUL characters."));
        if (definition.Kind == "bool" && value is not ("true" or "false")) throw new ArgumentException(UiText.Get("Boolean values must be true or false."));
        if (definition.Kind == "number" && !decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new ArgumentException(UiText.Format("{0} must be a number.", UiText.Get(definition.Label)));
        if (definition.Kind is "choice" or "preset")
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var choice = definition.Kind == "preset" ? parts.FirstOrDefault() : value;
            if (!Choices(command, game).Any(x => x.Value == choice) || definition.Kind == "preset" && (parts.Length > 2 || parts.Length == 2 && parts[1] is not ("true" or "false")))
                throw new ArgumentException(UiText.Format("Select a valid value for {0}.", UiText.Get(definition.Label)));
        }
    }
    public static bool Equal(string left, string right)
    {
        if (bool.TryParse(left, out var a) && bool.TryParse(right, out var b)) return a == b;
        if (decimal.TryParse(left, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
            decimal.TryParse(right, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var m)) return n == m;
        return left == right;
    }
    public static string StartupConfig(ServerProfile profile)
    {
        lock (profile.Sync)
        {
            var output = new StringBuilder("// Generated by LightRcon+\r\n");
            var password = profile.IsLayer ? "*MUST REPLACE TO YOUR RCON PASSWORD*" : SecretProtection.Unprotect(profile.ProtectedPassword);
            output.AppendLine("admin.password " + CommandText.Quote(password));
            var settings = new Dictionary<string, string>(profile.DesiredSettings);
            foreach (var draft in profile.DraftSettings) settings[draft.Key] = draft.Value;
            foreach (var entry in settings.Where(x => Supports(x.Key, profile.Game)).OrderBy(x => x.Key == "vars.serverType" ? 0 : x.Key == "vars.preset" ? 1 : 2))
                output.AppendLine(string.Join(" ", SetCommand(entry.Key, entry.Value).Select((word, index) => index == 0 ? word : CommandText.Quote(word))));
            return output.ToString();
        }
    }
}

public static class CommandText
{
    public static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    public static string[] Parse(string text)
    {
        List<string> words = [];
        var word = new StringBuilder();
        bool quoted = false, escaped = false, started = false;
        foreach (var c in text)
        {
            if (escaped) { word.Append(c); escaped = false; started = true; }
            else if (c == '\\' && quoted) escaped = true;
            else if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (started) { words.Add(word.ToString()); word.Clear(); started = false; } }
            else { word.Append(c); started = true; }
        }
        if (quoted || escaped) throw new ArgumentException(UiText.Get("Incomplete quote or escape sequence."));
        if (started) words.Add(word.ToString());
        if (words.Count == 0) throw new ArgumentException(UiText.Get("Enter an RCON command."));
        return words.ToArray();
    }
}
