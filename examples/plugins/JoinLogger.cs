using LightRcon.Core.Plugins;

[assembly: LightRconPlus_only(1)]

public sealed class JoinLogger : ILightRconPlugin
{
    private PluginContext context = null!;
    public void Enable(PluginContext context) => this.context = context;
    public void OnEvent(string[] words)
    {
        if (words.Length >= 2 && words[0] == "player.onJoin") context.Log(words[1] + " joined");
    }
    public void Dispose() { }
}
