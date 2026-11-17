using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class FormDialog : Window
{
    public StackPanel Fields { get; } = new();
    public Func<bool>? Save { get; set; }
    private readonly TextBlock error = new() { Foreground = new SolidColorBrush(Color.FromRgb(179, 38, 30)), TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    public FormDialog(Window owner, string title, string description, string accept = "Save", double width = 570)
    {
        Style = (Style)FindResource(typeof(Window));
        title = UiText.Get(title); description = UiText.Get(description); accept = UiText.Get(accept);
        Owner = owner; Title = title; Width = width; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(600, SystemParameters.WorkArea.Height - 60);
        var root = new StackPanel { Margin = new(20) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) });
        root.Children.Add(new TextBlock { Text = description, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20) });
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(350, SystemParameters.WorkArea.Height - 320) });
        root.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        var cancel = new Button { Content = UiText.Get("Cancel"), IsCancel = true }; actions.Children.Add(cancel);
        var save = new Button { Content = accept, Style = (Style)FindResource("PrimaryButton"), IsDefault = true, Margin = new(0) };
        save.Click += (_, _) => { try { if (Save?.Invoke() != false) DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
        actions.Children.Add(save); root.Children.Add(actions); Content = root;
    }
    public T Field<T>(string label, T control) where T : FrameworkElement
    {
        Fields.Children.Add(new TextBlock { Text = UiText.Get(label), Margin = new(0, 12, 0, 7), Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
        Fields.Children.Add(control); return control;
    }
    public static string? Input(Window owner, string title, string label, string value)
    {
        var dialog = new FormDialog(owner, title, label);
        var input = new TextBox { Text = value }; dialog.Fields.Children.Add(input);
        return dialog.ShowDialog() == true ? input.Text : null;
    }
    public static (string Duration, string Amount, string Reason)? Ban(Window owner, BanEntry ban)
    {
        var dialog = new FormDialog(owner, UiText.Get("Edit ban"), $"{ban.IdType}: {ban.Identifier}");
        var type = dialog.Field("Type", new ComboBox { ItemsSource = new[] { "perm", "seconds", "rounds" }.Select(x => new LocalizedChoice(x, x)).ToArray(), SelectedValuePath = "Code", SelectedValue = ban.BanType });
        var amount = dialog.Field(UiText.Get("Seconds / rounds"), new TextBox { Text = ban.BanType == "rounds" ? ban.Rounds : ban.Seconds });
        var reason = dialog.Field("Reason", new TextBox { Text = ban.Reason });
        dialog.Save = () => { if ((string?)type.SelectedValue != "perm" && (!int.TryParse(amount.Text, out var n) || n < 1)) throw new ArgumentException(UiText.Get("Duration must be a positive integer.")); return true; };
        return dialog.ShowDialog() == true ? ((string)type.SelectedValue, amount.Text, reason.Text) : null;
    }
}

public sealed class PermissionEditor : StackPanel
{
    private readonly Dictionary<LayerPermission, CheckBox> boxes = [];
    public PermissionEditor(LayerPermission initial)
    {
        var names = new Dictionary<LayerPermission, string>
        {
            [LayerPermission.View] = UiText.Get("View · Sign in, read status and chat history"), [LayerPermission.Chat] = UiText.Get("Chat · Send Say / Yell messages"),
            [LayerPermission.Players] = UiText.Get("Players · Player actions and special slots"), [LayerPermission.Bans] = UiText.Get("Bans · Manage bans"),
            [LayerPermission.Maps] = UiText.Get("Maps · Map lists, restart and next round"), [LayerPermission.Settings] = UiText.Get("Settings · Change server settings"),
            [LayerPermission.Console] = UiText.Get("Console · Use console (command permissions still apply)"), [LayerPermission.LayerAdmin] = UiText.Get("LayerAdmin · Manage Layer accounts and permissions")
        };
        foreach (var item in names)
        {
            var box = new CheckBox { Content = item.Value, IsChecked = initial.HasFlag(item.Key) };
            boxes[item.Key] = box; Children.Add(box);
        }
    }
    public LayerPermission Value => boxes.Where(x => x.Value.IsChecked == true).Aggregate(LayerPermission.None, (mask, item) => mask | item.Key);
}
