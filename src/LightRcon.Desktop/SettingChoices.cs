using System.Windows;
using System.Windows.Controls;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class SettingChoiceBox : ComboBox
{
    private (string Command, GameId Game)? catalog;
    public SettingChoiceBox(string command)
    {
        Style = (Style)FindResource(typeof(ComboBox));
        SelectedValuePath = "Tag";
        SetGame(command, GameId.BF4);
    }
    public void SetGame(string command, GameId game)
    {
        if (catalog == (command, game)) return;
        catalog = (command, game);
        Items.Clear();
        foreach (var choice in SettingsCatalog.Choices(command, game))
        {
            var item = new ComboBoxItem { Tag = choice.Value };
            LocExtension.Bind(item, ContentControl.ContentProperty, choice.Label); Items.Add(item);
        }
    }
    public string Value
    {
        get => SelectedValue as string ?? "";
        set
        {
            if (value.Length == 0) { SelectedIndex = -1; return; }
            // Preserve a future server value until the administrator chooses a known replacement.
            if (!Items.Cast<ComboBoxItem>().Any(x => (string)x.Tag == value)) Items.Add(new ComboBoxItem { Tag = value, Content = value });
            SelectedValue = value;
        }
    }
}

public sealed class PresetInput : UserControl
{
    public SettingChoiceBox Preset { get; } = new("vars.preset");
    public CheckBox Locked { get; } = new();
    public event EventHandler? ValueChanged;
    public PresetInput()
    {
        var panel = new StackPanel();
        LocExtension.Bind(Locked, ContentControl.ContentProperty, "Lock preset related values");
        panel.Children.Add(Preset); panel.Children.Add(Locked); Content = panel;
        Preset.SelectionChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        Locked.Checked += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        Locked.Unchecked += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
    }
    public string Value
    {
        get => Preset.Value + " " + (Locked.IsChecked == true ? "true" : "false");
        set { var (preset, locked) = SettingsCatalog.ParsePreset(value); Preset.Value = preset; Locked.IsChecked = locked; }
    }
}
