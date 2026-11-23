using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class PluginConsoleEditor : LogEditor
{
    // PRoCon CodRichTextBox palette, including its maroon error messages.
    private static readonly Brush[] Palette = [Brushes.Black, Brushes.Maroon, Brushes.MediumSeaGreen,
        Brushes.DarkOrange, Brushes.RoyalBlue, Brushes.CornflowerBlue, Brushes.DarkViolet,
        Brushes.DeepPink, Brushes.Red, Brushes.Gray];
    private ConsoleEntry[] entries = [];
    public override void Clear() { entries = []; base.Clear(); }
    public void UpdateEntries(ConsoleEntry[] next)
    {
        if (entries.SequenceEqual(next)) return;
        entries = next.ToArray();
        var inlines = new List<Inline>();
        foreach (var entry in next)
        {
            if (inlines.Count > 0) inlines.Add(new LineBreak());
            inlines.Add(new Run($"[{entry.Timestamp.LocalDateTime:HH:mm:ss}] [{entry.Kind}] "));
            inlines.AddRange(Parse(entry.Text));
        }
        ReplaceContent(inlines, 3000);
    }
    internal static IEnumerable<Run> Parse(string text)
    {
        var runs = new List<Run>();
        var buffer = new StringBuilder();
        Brush? color = null;
        var weight = FontWeights.Normal;
        var style = FontStyles.Normal;
        void Flush()
        {
            if (buffer.Length == 0) return;
            var run = new Run(buffer.ToString()) { FontWeight = weight, FontStyle = style };
            // ^0 uses the theme's normal text color so it is readable in dark mode.
            if (color is not null) run.Foreground = color;
            runs.Add(run); buffer.Clear();
        }
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '^' || index + 1 == text.Length) { buffer.Append(text[index]); continue; }
            var code = text[index + 1];
            if (code == '^') { buffer.Append('^'); index++; continue; }
            if (code is not (>= '0' and <= '9') && code is not ('b' or 'i' or 'n')) { buffer.Append('^'); continue; }
            Flush(); index++;
            if (code is >= '0' and <= '9') color = code == '0' ? null : Palette[code - '0'];
            else
            {
                weight = code == 'b' ? FontWeights.Bold : FontWeights.Normal;
                style = code == 'i' ? FontStyles.Italic : FontStyles.Normal;
            }
        }
        Flush();
        return runs;
    }
}
