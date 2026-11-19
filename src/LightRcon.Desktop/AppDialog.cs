using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightRcon.Core;

namespace LightRcon.Desktop;

internal sealed class AppDialog : Window
{
    internal StackPanel Fields { get; } = new();
    private readonly Grid root = new() { Margin = new(24) };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0) };
    internal string? Choice { get; private set; }
    internal AppDialog(Window? owner, string title, string message, double width = 620)
    {
        Style = (Style)FindResource(typeof(Window)); if (owner is { IsVisible: true }) Owner = owner; Title = UiText.Get(title);
        Width = width; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 60;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 14) });
        if (message.Length > 0) heading.Children.Add(new TextBlock { Text = UiText.Get(message), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new(0, 0, 0, 14) });
        root.Children.Add(heading);
        Grid.SetRow(Fields, 1); Grid.SetRow(actions, 2);
        root.Children.Add(Fields); root.Children.Add(actions); Content = root;
    }
    internal void AddAction(string label, string value, bool primary = false, bool cancel = false)
    {
        var button = new Button { Content = UiText.Get(label), IsCancel = cancel, IsDefault = primary, MinHeight = 34, MaxWidth = 250 };
        if (primary) button.Style = (Style)FindResource("PrimaryButton");
        button.Click += (_, _) => { Choice = value; DialogResult = !cancel; };
        actions.Children.Add(button);
    }
    internal static bool Confirm(Window owner, string message)
    {
        var dialog = new AppDialog(owner, "Confirm action", message);
        dialog.AddAction("Cancel", "cancel", cancel: true); dialog.AddAction("Confirm", "confirm", true);
        return dialog.ShowDialog() == true;
    }
    internal static void Alert(Window? owner, string message)
    {
        var dialog = new AppDialog(owner, "Operation failed", message);
        dialog.AddAction("OK", "ok", true); dialog.ShowDialog();
    }
    internal static string? ImportChoice(Window owner, string filename)
    {
        var dialog = new AppDialog(owner, "Import plugin", "Choose where to load this plugin. You can change the import preference in Settings.", 820);
        dialog.Fields.Children.Add(new TextBlock { Text = filename, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        dialog.AddAction("Keep plugin in its original location", "keep"); dialog.AddAction("Copy to plugin folder", "copy", true); dialog.AddAction("Cancel", "cancel", cancel: true);
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }
    internal static string? EditText(Window owner, string title, string text, bool readOnly)
    {
        var dialog = new AppDialog(owner, title, "", 900) { ResizeMode = ResizeMode.CanResize, SizeToContent = SizeToContent.Manual, Height = Math.Min(620, SystemParameters.WorkArea.Height - 60), MinWidth = 560, MinHeight = 420 };
        var input = new TextBox { Text = text, AcceptsReturn = true, AcceptsTab = true, IsReadOnly = readOnly, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.root.Children.Remove(dialog.Fields); Grid.SetRow(input, 1); dialog.root.Children.Add(input);
        dialog.AddAction("Cancel", "cancel", cancel: true); dialog.AddAction(readOnly ? "Close" : "Apply", "apply", true);
        return dialog.ShowDialog() == true && !readOnly ? input.Text : null;
    }
}
