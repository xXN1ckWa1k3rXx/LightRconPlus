using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using LightRcon.Core;

namespace LightRcon.Desktop;

public class LogEditor : RichTextBox
{
    private bool followTail = true;
    private bool replacing;
    private bool scrollScheduled;
    public string Text { get; private set; } = "";
    public string SelectedText => Selection.Text;

    public LogEditor()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        SetResourceReference(StyleProperty, typeof(RichTextBox));
        Document.PagePadding = new Thickness(0);
        Document.Blocks.Clear();
        SizeChanged += (_, _) => { UpdatePageWidth(); ScheduleTail(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleTail(); };
        PreviewMouseWheel += (_, e) => { if (e.Delta > 0) followTail = false; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.Home) followTail = false;
        };
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (replacing) return;
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) ScheduleTail();
            else if (e.VerticalChange != 0 && !scrollScheduled)
                followTail = VerticalOffset >= Math.Max(0, ExtentHeight - ViewportHeight) - 2;
        }));
    }

    public virtual void Clear()
    {
        Text = ""; followTail = true;
        Document.Blocks.Clear();
    }

    public new void ScrollToEnd()
    {
        followTail = true;
        ScheduleTail();
    }

    private void ScheduleTail()
    {
        if (!followTail || !Selection.IsEmpty || scrollScheduled) return;
        scrollScheduled = true;
        // RichTextBox queues caret/layout scrolling after a document replacement.
        // Follow only after that work and repeat when its extent changes.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            try { if (followTail && Selection.IsEmpty && IsVisible) { UpdateLayout(); base.ScrollToEnd(); } }
            finally { scrollScheduled = false; }
        }));
    }

    protected void ReplaceContent(List<Inline> inlines, int limit)
    {
        if (Text.Length == 0) followTail = true;
        var follow = followTail && Selection.IsEmpty;
        var offset = VerticalOffset;
        var start = Document.ContentStart.GetOffsetToPosition(Selection.Start);
        var end = Document.ContentStart.GetOffsetToPosition(Selection.End);
        foreach (var run in inlines.OfType<Run>()) run.Text = run.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var text = string.Concat(inlines.Select(inline => inline is Run run ? run.Text : "\n"));
        var lines = text.Split('\n');
        var skip = lines.Take(Math.Max(0, lines.Length - limit)).Sum(line => line.Length + 1);
        Text = text[Math.Min(skip, text.Length)..].Replace("\n", Environment.NewLine);
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        foreach (var inline in inlines)
        {
            var length = inline is Run run ? run.Text.Length : 1;
            if (skip >= length) { skip -= length; continue; }
            if (inline is Run retained && skip > 0) retained.Text = retained.Text[skip..];
            skip = 0;
            paragraph.Inlines.Add(inline);
        }
        replacing = true;
        BeginChange();
        try
        {
            Document.Blocks.Clear();
            if (Text.Length > 0) Document.Blocks.Add(paragraph);
            var maximum = Document.ContentStart.GetOffsetToPosition(Document.ContentEnd);
            Selection.Select(Document.ContentStart.GetPositionAtOffset(Math.Min(start, maximum))!, Document.ContentStart.GetPositionAtOffset(Math.Min(end, maximum))!);
        }
        finally
        {
            EndChange();
            UpdatePageWidth();
            UpdateLayout();
            if (!follow) ScrollToVerticalOffset(offset);
            replacing = false;
        }
        if (follow) ScheduleTail();
    }

    public void Select(int start, int length)
    {
        TextPointer Position(int index)
        {
            var pointer = Document.ContentStart;
            var remaining = index;
            while (pointer.CompareTo(Document.ContentEnd) < 0)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var count = pointer.GetTextRunLength(LogicalDirection.Forward);
                    if (remaining <= count) return pointer.GetPositionAtOffset(remaining)!;
                    remaining -= count;
                    pointer = pointer.GetPositionAtOffset(count)!;
                }
                else
                {
                    if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                        pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) remaining = Math.Max(0, remaining - Environment.NewLine.Length);
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward) ?? Document.ContentEnd;
                }
            }
            return Document.ContentEnd;
        }
        Selection.Select(Position(start), Position(start + length));
    }

    private void UpdatePageWidth()
    {
        var width = Text.Length == 0 ? 0 : new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        Document.PageWidth = Math.Max(Math.Max(1, ActualWidth - Padding.Left - Padding.Right - 22), width + 4);
    }
}

public sealed class ActivityEditor : LogEditor
{
    private ActivityEntry[] entries = [];
    private string language = "";
    private int lineLimit;
    public override void Clear() { entries = []; base.Clear(); }
    public void UpdateEntries(ActivityEntry[] next, int limit)
    {
        if (entries.SequenceEqual(next) && language == UiText.Language && lineLimit == limit) return;
        entries = next.ToArray(); language = UiText.Language; lineLimit = limit;
        var inlines = new List<Inline>();
        foreach (var entry in next)
        {
            if (inlines.Count > 0) inlines.Add(new LineBreak());
            var line = new ActivityLine { Entry = entry };
            var runs = line.Inlines.ToArray(); line.Inlines.Clear();
            inlines.AddRange(runs);
        }
        ReplaceContent(inlines, limit);
    }
}
