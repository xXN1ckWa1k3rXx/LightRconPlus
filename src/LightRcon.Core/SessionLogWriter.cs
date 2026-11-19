using System.Diagnostics;
using System.Text;

namespace LightRcon.Core;

internal sealed class SessionLogWriter(ProfileStore store)
{
    private static readonly string Launch = Process.GetCurrentProcess().StartTime.ToString("yyyy-MM-dd_HH-mm-ss-fff");
    private string? directory;
    private string connection = "";
    private long lastActivitySequence;
    public void Begin(string serverName, Func<ActivityEntry[]> snapshot)
    {
        lock (store.LogGate)
        {
            var safeName = string.Concat(serverName.Select(character => Path.GetInvalidFileNameChars().Contains(character) || char.IsControl(character) ? '_' : character)).Trim().TrimEnd('.');
            if (safeName.Length == 0) safeName = "Server";
            if (safeName.Length > 100) safeName = safeName[..100];
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(safeName.Split('.')[0], StringComparer.OrdinalIgnoreCase)) safeName = "_" + safeName;
            directory = Path.Combine(store.LogsDirectory, safeName, Launch);
            connection = DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss-fffffff");
            Directory.CreateDirectory(directory);
            foreach (var entry in snapshot()) WriteActivity(entry);
        }
    }
    public void WriteActivity(ActivityEntry entry)
    {
        lock (store.LogGate)
        {
            if (directory is null || entry.Sequence <= lastActivitySequence) return;
            Write(entry.Kind == "Chat" ? "chat" : "events", entry.Formatted);
            lastActivitySequence = entry.Sequence;
        }
    }
    public void Write(string category, string line)
    {
        lock (store.LogGate)
        {
            if (directory is null) return;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, category + "-" + connection + ".log"), line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
