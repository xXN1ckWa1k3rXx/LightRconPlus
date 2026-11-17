using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LightRcon.Core;

public sealed class LayerFrame
{
    public string Type { get; set; } = "";
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string[] Words { get; set; } = [];
    public ActivityEntry[] Entries { get; set; } = [];
    public string? Error { get; set; }
    public LayerPermission Permissions { get; set; }
    public int Protocol { get; set; } = 1;
    public PublicLayerOptions? Layer { get; set; }
}

public sealed record PublicLayerAccount(string Username, bool Enabled, LayerPermission? Permissions);
public sealed record PublicLayerOptions(bool Enabled, string BindAddress, int Port, LayerPermission GlobalPermissions, PublicLayerAccount[] Accounts);

public static class LayerWire
{
    public const int MaxFrameBytes = 512 * 1024;
    public static async Task<LayerFrame> ReadAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 2 or > MaxFrameBytes) throw new InvalidDataException("Invalid Layer frame size.");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        var frame = JsonSerializer.Deserialize<LayerFrame>(data) ?? throw new InvalidDataException("Empty Layer frame.");
        if (frame.Type is null || frame.Type.Length > 64 || frame.Username is null || frame.Password is null || frame.Words is null || frame.Entries is null ||
            frame.Words.Length > 4096 || frame.Words.Any(x => x is null || x.Contains('\0')) || frame.Entries.Length > 5000 ||
            frame.Entries.Any(x => x is null || x.Kind is null || x.Name is null || x.Team is null || x.Text is null) ||
            (frame.Layer is not null && (frame.Layer.Accounts is null || frame.Layer.Accounts.Any(x => x is null || x.Username is null))))
            throw new InvalidDataException("Malformed Layer frame fields.");
        return frame;
    }
    public static async Task WriteAsync(Stream stream, LayerFrame frame, SemaphoreSlim gate, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        if (bytes.Length > MaxFrameBytes) throw new InvalidDataException("Layer frame exceeds limit.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await gate.WaitAsync(token);
        try { await stream.WriteAsync(prefix, token); await stream.WriteAsync(bytes, token); }
        finally { gate.Release(); }
    }
    public static string Fingerprint(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    public static string NormalizePin(string pin) => pin.Replace(":", "").Replace(" ", "").ToUpperInvariant();
}

public static class LayerAuthorization
{
    public static LayerPermission Required(string[] words)
    {
        if (words.Length == 0) throw new UnauthorizedAccessException("Empty command.");
        var command = words[0];
        // Explicit allowlist. Console permission never bypasses passwords or account boundaries.
        if (command is "serverInfo" or "version" or "currentLevel" or "admin.help" or "admin.listPlayers" or "listPlayers" or
            "mapList.list" or "mapList.availableMaps" or "mapList.getMapIndices" or "mapList.getRounds" or
            "reservedSlotsList.list" or "spectatorList.list" or "punkBuster.isActive" or "fairFight.isActive" or "banList.list")
            return LayerPermission.View;
        if (SettingsCatalog.Find(command) is not null) return words.Length == 1 ? LayerPermission.View : LayerPermission.Settings;
        if (command is "admin.say" or "admin.yell") return LayerPermission.Chat;
        if (command is "admin.killPlayer" or "admin.kickPlayer" or "admin.movePlayer") return LayerPermission.Players;
        if (command is "banList.add" or "banList.remove" or "banList.clear" or "banList.save" or "banList.load") return LayerPermission.Bans;
        if (command is "mapList.add" or "mapList.remove" or "mapList.clear" or "mapList.save" or "mapList.load" or
            "mapList.restartRound" or "mapList.runNextRound" or "mapList.setNextMapIndex" or "mapList.endRound") return LayerPermission.Maps;
        if (command is "reservedSlotsList.add" or "reservedSlotsList.remove" or "reservedSlotsList.clear" or "reservedSlotsList.save" or
            "spectatorList.add" or "spectatorList.remove" or "spectatorList.clear" or "spectatorList.save") return LayerPermission.Players;
        if (command == "punkBuster.pb_sv_command" && words.Length == 2)
        {
            // Deny arbitrary PB commands, config execution, and commands concatenated by separators.
            var pb = words[1];
            if (pb.IndexOfAny(['\r', '\n', ';', '\0']) >= 0) throw new UnauthorizedAccessException("Invalid PunkBuster command.");
            var tokens = CommandText.Parse(pb);
            return tokens[0].ToLowerInvariant() switch
            {
                "pb_sv_plist" when tokens.Length == 1 => LayerPermission.View,
                "pb_sv_kick" => LayerPermission.Players,
                "pb_sv_ban" or "pb_sv_banguid" or "pb_sv_unbanguid" or "pb_sv_updbanfile" => LayerPermission.Bans,
                _ => throw new UnauthorizedAccessException(UiText.Get("This PunkBuster command is not available through Layer."))
            };
        }
        if (command is "player.ping" or "player.isAlive" or "player.idleDuration") return LayerPermission.View;
        throw new UnauthorizedAccessException(UiText.Get("This command is not available through Layer."));
    }
    public static void Demand(LayerPermission actual, LayerPermission required)
    {
        if (!actual.HasFlag(LayerPermission.View) || !actual.HasFlag(required)) throw new UnauthorizedAccessException(UiText.Get("Insufficient Layer permission: ") + required);
    }
}
