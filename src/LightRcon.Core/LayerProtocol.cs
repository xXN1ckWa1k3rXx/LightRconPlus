using System.Buffers.Binary;
using System.Buffers;
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
    public ConsoleEntry[] ConsoleEntries { get; set; } = [];
    public string? Error { get; set; }
    public LayerPermission Permissions { get; set; }
    public int Protocol { get; set; } = 1;
    public PublicLayerOptions? Layer { get; set; }
    public Plugins.PluginDetails[] Plugins { get; set; } = [];
    public Plugins.PluginDetails? Plugin { get; set; }
    public Plugins.PluginSetting[] PluginSettings { get; set; } = [];
    public bool SupportsSnapshots { get; set; }
    public LayerSnapshot? Snapshot { get; set; }
    public bool SupportsMapUpdates { get; set; }
    public LayerMapUpdate? Maps { get; set; }
    public MapEntry[]? ExpectedMaps { get; set; }
}

public sealed record LayerMapUpdate(long Revision, MapEntry[] Entries);

// Read-only state owned by the host; mutations always go through its RCON connection.
public sealed class LayerSnapshot
{
    public GameId Game { get; set; }
    public ServerInfo Info { get; set; } = ServerInfo.Empty;
    public PlayerInfo[] Players { get; set; } = [];
    public MapEntry[] Maps { get; set; } = [];
    public long MapsRevision { get; set; }
    public Dictionary<string, string> Settings { get; set; } = [];
    public string[] Unsupported { get; set; } = [];
    public string[] Commands { get; set; } = [];
    internal bool IsValid => Enum.IsDefined(Game) && Info is not null && Info.TeamScores is not null &&
        Players is not null && Players.Length <= 512 && Players.All(x => x is not null && x.Name is not null && x.Ip is not null) &&
        Maps is not null && Maps.Length <= 10000 && Maps.All(x => x is not null && x.FileName is not null && x.GameMode is not null) &&
        Settings is not null && Settings.Count <= 512 && Settings.All(x => x.Value is not null) &&
        Unsupported is not null && Unsupported.Length <= 512 && Unsupported.All(x => x is not null) &&
        Commands is not null && Commands.Length <= 4096 && Commands.All(x => x is not null);
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
        if (frame.Type is null || frame.Type.Length > 64 || frame.Username is null || frame.Password is null || frame.Words is null || frame.Entries is null || frame.ConsoleEntries is null ||
            frame.ConsoleEntries.Length > 3000 || frame.ConsoleEntries.Any(entry => entry is null || entry.Kind is null || entry.Text is null) ||
            frame.Plugins is null || frame.PluginSettings is null || frame.Plugins.Any(x => x is null || x.Path is null || x.Name is null || x.Kind is null || x.Error is null) ||
            frame.PluginSettings.Any(x => x is null || x.Name is null || x.Type is null || x.Value is null) ||
            frame.Words.Length > 4096 || frame.Words.Any(x => x is null || x.Contains('\0')) || frame.Entries.Length > 5000 ||
            frame.Entries.Any(x => x is null || x.Kind is null || x.Name is null || x.Team is null || x.Text is null) ||
            (frame.Snapshot is not null && !frame.Snapshot.IsValid) ||
            (frame.Maps is not null && !ValidMaps(frame.Maps.Entries)) ||
            (frame.ExpectedMaps is not null && !ValidMaps(frame.ExpectedMaps)) ||
            (frame.Layer is not null && (frame.Layer.Accounts is null || frame.Layer.Accounts.Any(x => x is null || x.Username is null))))
            throw new InvalidDataException("Malformed Layer frame fields.");
        return frame;
    }
    private static bool ValidMaps(MapEntry[] maps) => maps is not null && maps.Length <= 1000 &&
        maps.All(map => map is not null && map.FileName is not null && map.GameMode is not null && map.Rounds is >= 1 and <= 100);
    public static async Task WriteAsync(Stream stream, LayerFrame frame, SemaphoreSlim gate, CancellationToken token)
    {
        using var packet = new FrameBuffer();
        using (var writer = new Utf8JsonWriter(packet)) JsonSerializer.Serialize(writer, frame);
        var length = packet.Length - 4;
        if (length > MaxFrameBytes) throw new InvalidDataException("Layer frame exceeds limit.");
        packet.SetLengthPrefix(length);
        await gate.WaitAsync(token);
        try { await stream.WriteAsync(packet.WrittenMemory, token); }
        finally { gate.Release(); }
    }
    // One pooled UTF-8 packet replaces two large byte[] allocations per Layer
    // frame. Clear returned storage because frames may contain login passwords.
    private sealed class FrameBuffer : IBufferWriter<byte>, IDisposable
    {
        private byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        public int Length { get; private set; } = 4;
        public ReadOnlyMemory<byte> WrittenMemory => buffer.AsMemory(0, Length);
        public void SetLengthPrefix(int length) => BinaryPrimitives.WriteInt32LittleEndian(buffer, length);
        public void Advance(int count) => Length += count;
        public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsMemory(Length); }
        public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsSpan(Length); }
        private void Ensure(int size)
        {
            size = Math.Max(1, size);
            if (buffer.Length - Length >= size) return;
            var next = ArrayPool<byte>.Shared.Rent(Math.Max(checked(Length + size), buffer.Length * 2));
            buffer.AsSpan(0, Length).CopyTo(next);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true); buffer = next;
        }
        public void Dispose() => ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
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
        if (command == "vars.teamFactionOverride")
        {
            if (words.Length == 1) return LayerPermission.View;
            if (words.Length != 3 || !SettingsCatalog.IsFactionOverride(SettingsCatalog.SettingKey(words))) throw new UnauthorizedAccessException("Invalid faction override command.");
            SettingsCatalog.Validate(SettingsCatalog.SettingKey(words), words[2]);
            return LayerPermission.Settings;
        }
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
