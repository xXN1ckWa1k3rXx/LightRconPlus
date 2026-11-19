namespace LightRcon.Core.Plugins;

public sealed record PluginDetails(string Path, string Name, string Version, string Author, string Website, string Description, string Kind)
{
    public bool Enabled { get; init; }
    public string Error { get; init; } = "";
    public string Status => UiText.Get(Error.Length > 0 ? "Plugin failed" : Enabled ? "Enabled" : "Disabled");
    public override string ToString() => Name;
}

public sealed record PluginSetting(string Name, string Type, string Value, bool ReadOnly = false)
{
    public string Group => Name.Contains('|') ? Name[..Name.LastIndexOf('|')] : "";
    public string Label => Name[(Name.LastIndexOf('|') + 1)..];
    public string[] Choices => Type.ToLowerInvariant() switch
    {
        "onoff" => ["On", "Off"], "yesno" => ["Yes", "No"],
        _ => Type.StartsWith("enum.", StringComparison.OrdinalIgnoreCase) && Type.Contains('(')
            ? Type[(Type.IndexOf('(') + 1)..].TrimEnd(')').Split('|') : []
    };
}

public interface IPluginConfiguration
{
    PluginDetails Details { get; }
    IReadOnlyList<PluginSetting> GetSettings();
    void SetSetting(string name, string value);
}

internal interface IPluginRuntime : IAsyncDisposable
{
    PluginDetails Details { get; }
    IReadOnlyList<PluginSetting> GetSettings();
    void SetSetting(string name, string value);
    void Enable();
    void Handle(string[] words);
    void Tick();
}
