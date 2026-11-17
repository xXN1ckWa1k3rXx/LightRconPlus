using System.Globalization;
using System.Text.Json;

namespace LightRcon.Core;

/// <summary>Interface language is independent of OS date/number preferences and protocol values.</summary>
public static class UiText
{
    private static readonly Dictionary<string, Dictionary<string, string>> Catalog = Load();
    private static string language = "en";
    public static string Language => language;
    public static event Action? LanguageChanged;
    public static IReadOnlyDictionary<string, Dictionary<string, string>> Translations => Catalog;
    public static void SetLanguage(string? value)
    {
        var normalized = value is "zh" or "de" ? value : "en";
        // Do not set CurrentCulture: chat date formatting must follow the PC's regional preferences.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(normalized == "zh" ? "zh-CN" : normalized);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        if (language == normalized) return;
        language = normalized;
        LanguageChanged?.Invoke();
    }
    public static string Get(string key)
        => Catalog.TryGetValue(key, out var entry) ? entry.GetValueOrDefault(language, entry.GetValueOrDefault("en", key)) : key;
    public static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("LightRcon.Core.Data.translations.json");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream!)!;
    }
}
