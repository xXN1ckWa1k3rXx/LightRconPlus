using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightRcon.Core;
using Microsoft.Win32;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, Button> settingApplyButtons = [];
    private void SettingChanged(Control editor)
    {
        if (!switching) { editor.Tag = true; dirty = true; }
    }
    private void BuildSettings()
    {
        foreach (var group in SettingsCatalog.All.GroupBy(x => x.Group))
        {
            var section = new StackPanel(); var heading = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 14) };
            LocExtension.Bind(heading, TextBlock.TextProperty, group.Key); section.Children.Add(heading);
            foreach (var definition in group)
            {
                var row = new Grid { Margin = new(0, 0, 0, 12) }; row.ColumnDefinitions.Add(new() { Width = new(235) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
                LocExtension.Bind(caption, TextBlock.TextProperty, definition.Label); label.Children.Add(caption);
                var commandLabel = new TextBlock { Text = definition.Command, FontSize = 10, Margin = new(0, 4, 0, 0) };
                commandLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); label.Children.Add(commandLabel); row.Children.Add(label);
                Control editor;
                if (definition.Kind == "bool")
                {
                    var check = new CheckBox { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                    check.Checked += (_, _) => SettingChanged(check); check.Unchecked += (_, _) => SettingChanged(check); editor = check;
                    check.Click += async (_, _) =>
                    {
                        if (ready && !switching && !closing && check.IsEnabled && check.IsChecked is not null)
                            await ApplySettingChangesAsync([definition.Command]);
                    };
                }
                else if (definition.Kind == "preset")
                {
                    var preset = new PresetInput { MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left };
                    preset.ValueChanged += (_, _) => SettingChanged(preset); editor = preset;
                }
                else if (definition.Kind == "choice")
                {
                    var combo = new SettingChoiceBox(definition.Command) { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
                    combo.SelectionChanged += (_, _) => SettingChanged(combo); editor = combo;
                }
                else if (definition.Command == "vars.gameModeCounter")
                {
                    var counter = new TicketCounterInput();
                    counter.ValueChanged += (_, _) => SettingChanged(counter);
                    editor = counter;
                }
                else if (definition.Kind == "number")
                {
                    var number = new NumberInput { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
                    number.ValueChanged += (_, _) => SettingChanged(number); editor = number;
                }
                else
                {
                    var text = new TextBox { MinHeight = definition.Kind == "multiline" ? 72 : 32, AcceptsReturn = definition.Kind == "multiline", TextWrapping = definition.Kind == "multiline" ? TextWrapping.Wrap : TextWrapping.NoWrap, TextAlignment = TextAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top };
                    text.TextChanged += (_, _) => SettingChanged(text); editor = text;
                }
                LocExtension.Bind(editor, System.Windows.Automation.AutomationProperties.NameProperty, definition.Label);
                editor.ToolTip = definition.Hint.Length > 0 ? definition.Hint : definition.Command;
                settingEditors[definition.Command] = editor; Grid.SetColumn(editor, 1); row.Children.Add(editor);
                if (definition.Kind != "bool")
                {
                    var apply = new Button { Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, MinWidth = 65 };
                    LocExtension.Bind(apply, ContentControl.ContentProperty, "Apply");
                    apply.Click += async (_, _) => await ApplySettingChangesAsync([definition.Command]);
                    settingApplyButtons[definition.Command] = apply; Grid.SetColumn(apply, 2); row.Children.Add(apply);
                }
                section.Children.Add(row);
            }
            settingSections[group.Key] = section;
            SettingsSections.Children.Add(new Border { Child = section, Style = (Style)FindResource("Panel"), Margin = new(0, 0, 0, 16) });
        }
        SettingsNavigation.ItemsSource = settingSections.Keys.Select(x => new LocalizedChoice(x, x)).ToArray(); SettingsNavigation.SelectedIndex = 0;
    }
    private void PopulateSettings(bool reset)
    {
        if (selected is null) return; var before = switching; switching = true; var profile = selected.Profile;
        lock (profile.Sync)
            foreach (var item in settingEditors)
            {
                if (!reset && item.Value.Tag is true) continue;
                var value = profile.DraftSettings.GetValueOrDefault(item.Key, profile.DesiredSettings.GetValueOrDefault(item.Key, ""));
                switch (item.Value)
                {
                    case TextBox text: text.Text = value; break;
                    case CheckBox check: check.IsChecked = bool.TryParse(value, out var enabled) ? enabled : null; break;
                    case SettingChoiceBox combo:
                        combo.SetGame(item.Key, selected.Game);
                        combo.Value = value; break;
                    case PresetInput preset: preset.Value = value; break;
                    case NumberInput number: number.Text = value; break;
                    case TicketCounterInput counter: counter.Text = value; break;
                }
                item.Value.Tag = profile.DraftSettings.ContainsKey(item.Key);
                item.Value.IsEnabled = selected.IsSupported(item.Key) && selected.Permissions.HasFlag(LayerPermission.Settings);
                if (settingApplyButtons.TryGetValue(item.Key, out var apply)) apply.IsEnabled = item.Value.IsEnabled;
                item.Value.ToolTip = UiText.Get(selected.IsSupported(item.Key) ? SettingsCatalog.Find(item.Key)?.Hint ?? "" : "Setting unavailable or account has no permission to read it.");
            }
        UpdateTicketContext(); switching = before;
    }
    private void UpdateTicketContext()
    {
        if (selected is null || !settingEditors.TryGetValue("vars.gameModeCounter", out var editor) || editor is not TicketCounterInput counter) return;
        counter.SetContext(selected.Info.Map, selected.Info.GameMode, selected.Game);
    }
    private static string EditorValue(Control editor) => editor switch
    {
        TextBox text => text.Text,
        CheckBox check => check.IsChecked is bool value ? value ? "true" : "false" : "",
        SettingChoiceBox combo => combo.Value,
        PresetInput preset => preset.Value,
        NumberInput number => number.Text,
        TicketCounterInput counter => counter.Text,
        _ => ""
    };
    private bool updatingOutline;
    private void SettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingOutline || SettingsNavigation.SelectedItem is not LocalizedChoice choice || !settingSections.TryGetValue(choice.Code, out var element) || !element.IsLoaded) return;
        SettingsScroll.ScrollToVerticalOffset(element.TranslatePoint(new Point(), SettingsSections).Y);
    }
    private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ready || !SettingsScroll.IsVisible || settingSections.Count == 0 || e.Source != SettingsScroll) return;
        var section = settingSections.First().Key;
        foreach (var item in settingSections)
            if (item.Value.TranslatePoint(new Point(), SettingsSections).Y <= SettingsScroll.VerticalOffset + 20) section = item.Key;
        if (SettingsScroll.ScrollableHeight > 0 && SettingsScroll.VerticalOffset >= SettingsScroll.ScrollableHeight - 2) section = settingSections.Last().Key;
        updatingOutline = true;
        SettingsNavigation.SelectedItem = SettingsNavigation.Items.Cast<LocalizedChoice>().FirstOrDefault(x => x.Code == section);
        updatingOutline = false;
    }
    private async Task ApplySettingChangesAsync(IEnumerable<string> keys)
    {
        var s = selected; if (s is null) return;
        await Run(async () =>
        {
            var changes = keys.Where(key => settingEditors[key].IsEnabled).ToDictionary(key => key, key => EditorValue(settingEditors[key]));
            if (changes.Count == 0) return;
            await s.ApplySettingsAsync(changes);
            lock (s.Profile.Sync)
                foreach (var entry in changes)
                    if (!s.Profile.DraftSettings.TryGetValue(entry.Key, out var draft) || draft == entry.Value) s.Profile.DraftSettings.Remove(entry.Key);
            if (selected == s) foreach (var entry in changes) if (EditorValue(settingEditors[entry.Key]) == entry.Value) settingEditors[entry.Key].Tag = false;
            store.Save(); FooterStatus.Text = UiText.Get("Local settings saved. See Console for synchronization results.");
        });
    }
    private void ServerSettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready && e.Source == ServerSettingsTabs) UpdateStartupConfig();
    }
    private void UpdateStartupConfig()
    {
        if (selected is null || ServerSettingsTabs.SelectedIndex != 1) return;
        SaveClient();
        var config = SettingsCatalog.StartupConfig(selected.Profile);
        if (StartupConfigText.Text != config) StartupConfigText.Text = config;
    }
    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        if (selected is null) return;
        SaveClient();
        if (!selected.Profile.IsLayer && !Confirm(UiText.Get("The direct connection config will contain the RCON password. Continue exporting?"))) return;
        Export("Startup.txt", SettingsCatalog.StartupConfig(selected.Profile));
    }
    private void ExportMapList_Click(object sender, RoutedEventArgs e)
    {
        if (selected is null) return; var maps = selected.Profile.DesiredMaps ?? selected.MapSnapshot().ToList();
        Export("MapList.txt", string.Join("\r\n", maps.Select(x => $"{x.FileName} {x.GameMode} {x.Rounds}")) + "\r\n");
    }
    private void Export(string file, string text)
    {
        var dialog = new SaveFileDialog { FileName = file, Filter = UiText.Get("Text files (*.txt)|*.txt|All files (*.*)|*.*") };
        if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false)); FooterStatus.Text = UiText.Get("Exported ") + dialog.FileName; }
    }
}
