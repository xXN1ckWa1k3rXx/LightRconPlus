using System.Text.Json;

namespace LightRcon.Core;

/// <summary>Display names only; protocol values and journal records retain their original identifiers.</summary>
public static class GameText
{
    private static readonly Dictionary<GameId, Dictionary<string, string>> Weapons = Enum.GetValues<GameId>().ToDictionary(x => x,
        x => new Dictionary<string, string>(GameCatalog.Load<Dictionary<string, string>>(x, "weapons"), StringComparer.OrdinalIgnoreCase));
    public static string Chat(string text)
    {
        var id = text.Trim();
        if (!id.StartsWith("ID_CHAT_", StringComparison.OrdinalIgnoreCase)) return text;
        var key = id.ToUpperInvariant();
        // BF4 sends this Commo Rose ID with a slash; the reference catalog uses an underscore.
        if (key == "ID_CHAT_ATTACK/DEFEND") key = "ID_CHAT_ATTACK_DEFEND";
        var label = UiText.Get(key);
        return label == key ? text : label;
    }
    public static string Weapon(string id, GameId game = GameId.BF4)
    {
        if (!Weapons[game].TryGetValue(id, out var label)) return id;
        var separator = label.IndexOf(" under ", StringComparison.Ordinal);
        return separator > 0
            ? UiText.Format("{0} (underbarrel on {1})", UiText.Get(label[..separator]), label[(separator + 7)..])
            : UiText.Get(label);
    }
}
