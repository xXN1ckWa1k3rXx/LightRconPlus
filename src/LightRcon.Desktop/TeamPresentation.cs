using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class TeamHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup group) return "";
        var player = group.Items.OfType<PlayerInfo>().FirstOrDefault();
        var name = player?.TeamName is { Length: > 0 } known ? known : player?.Team == 0 ? UiText.Get("Unassigned / Spectators") : UiText.Format("Team {0}", player?.Team ?? 0);
        return UiText.Format("{0} players | {1}", group.ItemCount, name) + (player?.TeamTickets is int tickets ? UiText.Format(" — {0} tickets", tickets) : "");
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class TeamBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Application.Current.TryFindResource(value is int team && team is >= 1 and <= 4 ? "Team" + team : "Muted") ?? Brushes.Gray;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class ActivityLine : TextBlock
{
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(nameof(Entry), typeof(ActivityEntry), typeof(ActivityLine), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty LanguageVersionProperty = DependencyProperty.Register(nameof(LanguageVersion), typeof(int), typeof(ActivityLine), new PropertyMetadata(0, Changed));
    public ActivityEntry? Entry { get => (ActivityEntry?)GetValue(EntryProperty); set => SetValue(EntryProperty, value); }
    public int LanguageVersion { get => (int)GetValue(LanguageVersionProperty); set => SetValue(LanguageVersionProperty, value); }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e) => ((ActivityLine)target).Render();
    private void Render()
    {
        Inlines.Clear(); var entry = Entry; if (entry is null) return;
        var local = entry.Timestamp.LocalDateTime;
        var date = local.ToString("d", CultureInfo.CurrentCulture);
        Inlines.Add(new Run($"[{date} {local:HH:mm:ss}] "));
        void Name(string text, int team)
        {
            var run = new Run(text); run.SetResourceReference(TextElement.ForegroundProperty, team is >= 1 and <= 4 ? "Team" + team : "Accent"); Inlines.Add(run);
        }
        if (entry.Kind == "Chat")
        {
            var team = int.TryParse(entry.Team.Replace("Team ", "", StringComparison.Ordinal), out var id) ? id : 0;
            Name(entry.Name, team); Inlines.Add(new Run($" [{(team > 0 ? UiText.Format("Team {0}", team) : entry.Team)}] : "));
            Inlines.Add(new Run(entry.DisplayText) { ToolTip = entry.DisplayText != entry.Text ? entry.Text : null });
        }
        else if (entry.TryGetKillDetails(out var target, out var weapon))
        {
            Name(entry.Name, entry.ActorTeam); Inlines.Add(new Run(" " + UiText.Get("killed") + " "));
            Name(target, entry.TargetTeam);
            Inlines.Add(new Run(" " + UiText.Get("with") + " " + GameText.Weapon(weapon, entry.Game)) { ToolTip = weapon });
        }
        else if (entry.Kind is "Join" or "Leave")
        {
            Name(entry.Name, 0); Inlines.Add(new Run(" " + UiText.Get(entry.Kind == "Join" ? "connected to the server" : "disconnected from the server")));
        }
        else Inlines.Add(new Run(entry.Text));
    }
}
