using System.Text;
using System.Text.Json;

namespace LightRcon.Core;

public sealed class ActivityJournal
{
    private const int MemoryLimit = 5000;
    private readonly object gate = new();
    private readonly string directory;
    private readonly List<ActivityEntry> entries = [];
    private long sequence;
    private bool persistent;
    public event Action<ActivityEntry[]>? Appended;
    public ActivityJournal(string directory, bool persistent)
    {
        this.directory = directory;
        this.persistent = persistent;
        LoadRecent();
        if (!persistent) entries.Clear(); // Preserve monotonic IDs without displaying old direct-session history.
    }
    private void LoadRecent()
    {
        foreach (var file in Directory.EnumerateFiles(directory, "activity-*.jsonl").OrderByDescending(x => x).Take(7).Reverse())
            foreach (var line in File.ReadLines(file))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<ActivityEntry>(line);
                    if (entry is null) continue;
                    entries.Add(entry);
                    sequence = Math.Max(sequence, entry.Sequence);
                    if (entries.Count > MemoryLimit) entries.RemoveAt(0);
                }
                catch (JsonException) { /* A partial trailing record must not discard the rest of the journal. */ }
            }
    }
    public void SetPersistent(bool enabled)
    {
        lock (gate)
        {
            if (enabled && !persistent)
            {
                // Preserve the current session as soon as Layer hosting is enabled.
                persistent = true;
                Write(entries.ToArray());
            }
            persistent = enabled;
        }
    }
    public ActivityEntry[] Snapshot() { lock (gate) return entries.ToArray(); }
    public IEnumerable<ActivityEntry[]> Replay()
    {
        // Only the visible list is bounded; a Layer receives all persisted history, in order.
        if (!persistent) { foreach (var chunk in Snapshot().Chunk(100)) yield return chunk; yield break; }
        List<ActivityEntry> batch = [];
        foreach (var path in Directory.EnumerateFiles(directory, "activity-*.jsonl").OrderBy(x => x, StringComparer.Ordinal))
        {
            using var input = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            while (input.ReadLine() is { } line)
            {
                ActivityEntry? entry;
                try { entry = JsonSerializer.Deserialize<ActivityEntry>(line); } catch (JsonException) { continue; }
                if (entry is null) continue;
                batch.Add(entry);
                if (batch.Count < 100) continue;
                yield return batch.ToArray(); batch.Clear();
            }
        }
        if (batch.Count > 0) yield return batch.ToArray();
    }
    public void Add(string kind, string name, string team, string text, int actorTeam = 0, int targetTeam = 0, string targetName = "", string weapon = "", GameId game = GameId.BF4)
    {
        ActivityEntry entry;
        lock (gate)
        {
            entry = new(++sequence, DateTimeOffset.Now, kind, name, team, text)
            { ActorTeam = actorTeam, TargetTeam = targetTeam, TargetName = targetName, Weapon = weapon, Game = game };
            AppendLocked([entry]);
        }
        Appended?.Invoke([entry]);
    }
    public void Import(ActivityEntry[] history)
    {
        ActivityEntry[] fresh;
        lock (gate)
        {
            fresh = history.Where(x => x.Sequence > sequence).OrderBy(x => x.Sequence).ToArray();
            if (fresh.Length == 0) return;
            sequence = fresh[^1].Sequence;
            AppendLocked(fresh);
        }
        Appended?.Invoke(fresh);
    }
    private void AppendLocked(ActivityEntry[] incoming)
    {
        // Write before publishing so a downstream Layer can never see an uncommitted record.
        if (persistent) Write(incoming);
        entries.AddRange(incoming);
        if (entries.Count > MemoryLimit) entries.RemoveRange(0, entries.Count - MemoryLimit);
    }
    private void Write(ActivityEntry[] incoming)
    {
        foreach (var group in incoming.GroupBy(x => x.Timestamp.LocalDateTime.ToString("yyyy-MM-dd")))
        {
            var path = Path.Combine(directory, $"activity-{group.Key}.jsonl");
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            foreach (var entry in group) writer.WriteLine(JsonSerializer.Serialize(entry));
            writer.Flush();
            stream.Flush(true);
        }
    }
}
