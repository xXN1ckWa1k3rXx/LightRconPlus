using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private sealed record AudienceChoice(string Code, bool All = false)
    {
        public override string ToString() => All ? UiText.Get("All Players") : Code;
    }
    private string AudienceCode => (AudienceBox.SelectedItem as AudienceChoice)?.Code ?? "All Players";
    private void UpdateAudience(PlayerInfo[] players, string code)
    {
        var choices = new[] { new AudienceChoice("All Players", true) }.Concat(players.Select(x => new AudienceChoice(x.Name))).ToArray();
        if ((AudienceBox.ItemsSource as AudienceChoice[] ?? []).SequenceEqual(choices)) return;
        AudienceBox.ItemsSource = choices;
        AudienceBox.SelectedItem = choices.FirstOrDefault(x => x.Code == code) ?? choices[0];
    }
    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || closing || LanguageSelector.SelectedValue is not string code) return;
        SaveClient();
        UiText.SetLanguage(code); store.State.Language = UiText.Language; store.Save();
        var previous = switching; switching = true;
        var choice = ServerSelector.SelectedItem; serverChoices.Clear();
        foreach (var card in cards) serverChoices.Add(card);
        serverChoices.Add(new CreateServerChoice()); ServerSelector.SelectedItem = choice;
        var audience = AudienceCode; AudienceBox.ItemsSource = null;
        UpdateAudience(selected?.PlayerSnapshot() ?? [], audience);
        var section = (SettingsNavigation.SelectedItem as LocalizedChoice)?.Code;
        SettingsNavigation.ItemsSource = settingSections.Keys.Select(x => new LocalizedChoice(x, x)).ToArray();
        SettingsNavigation.SelectedItem = SettingsNavigation.Items.Cast<LocalizedChoice>().FirstOrDefault(x => x.Code == section);
        ServerEditorTitle.Text = UiText.Get(editingCard is null ? "Add server" : "Edit server");
        EditorPasswordLabel.Text = UiText.Get(editingCard is null ? "RCON / Layer password" : "RCON / Layer password (leave blank to keep)");
        OfflinePasswordLabel.Text = UiText.Get("RCON / Layer password (leave blank to keep)");
        if (displayedLayer is not null) GlobalPermissionsText.Text = UiText.Get("Global default permissions: ") + displayedLayer.GlobalPermissions;
        switching = previous; shownVersion = -1; RefreshView();
        UpdateMapGroups();
        UpdateSelectedPlayer();
    }
    private void ServerCards_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<Button>(e.OriginalSource as DependencyObject) is null && Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: ServerCard card }) SelectServer(card.Session);
    }
}
