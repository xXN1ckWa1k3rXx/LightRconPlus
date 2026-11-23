using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LightRcon.Core;

// Adapted from GeoIPCountry.cs, Copyright (C) 2008 MaxMind, Inc., GPL-2.0-or-later.
// Database license and provenance: docs/licenses/MaxMind-GeoLite.txt and THIRD-PARTY-NOTICES.md.
public static class GeoLocation
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> CountryNames = new(() =>
    {
        using var stream = typeof(GeoLocation).Assembly.GetManifestResourceStream("country-names.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    });
    private const int CountryStart = 16776960;
    private static readonly Lazy<byte[]> Database = new(() =>
    {
        using var stream = typeof(GeoLocation).Assembly.GetManifestResourceStream("GeoIP.dat")!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    });
    private static readonly Lazy<string[]> CountryCodes = new(() =>
    {
        using var stream = typeof(GeoLocation).Assembly.GetManifestResourceStream("geo-country-codes.json")!;
        return JsonSerializer.Deserialize<string[]>(stream)!;
    });
    public static string CountryCode(string address)
    {
        if (!IPAddress.TryParse(address, out var ip)) return "";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetwork) return "";
        var bytes = ip.GetAddressBytes();
        if (bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return "";
        var number = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        var data = Database.Value;
        var offset = 0;
        for (var bit = 31; bit >= 0; bit--)
        {
            var position = (long)offset * 6 + ((number >> bit & 1) == 0 ? 0 : 3);
            if (position < 0 || position + 3 > data.Length) return "";
            offset = data[position] | data[position + 1] << 8 | data[position + 2] << 16;
            if (offset < CountryStart) continue;
            var index = offset - CountryStart;
            return index > 0 && index < CountryCodes.Value.Length && CountryCodes.Value[index] != "--" ? CountryCodes.Value[index] : "";
        }
        return "";
    }
    public static string CountryName(string code)
    {
        if (code.Length == 0) return UiText.Get("Unknown region");
        // RegionInfo.DisplayName follows the installed Windows UI language.
        // Use the application's selected language, including on English Windows.
        return CountryNames.Value[UiText.Language].GetValueOrDefault(code.ToUpperInvariant(), UiText.Get("Unknown region"));
    }
}
