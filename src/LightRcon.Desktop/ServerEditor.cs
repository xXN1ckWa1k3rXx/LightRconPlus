using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private ServerCard? editingCard;
    private bool returnToWorkspace;
    private void ShowServerEditor(ServerCard? card)
    {
        if (closing) return;
        SaveClient(); store.Save();
        returnToWorkspace = WorkspacePage.Visibility == Visibility.Visible;
        editingCard = card;
        ServerEditorTitle.Text = UiText.Get(card is null ? "Add server" : "Edit server");
        EditorName.Text = card?.Session.Profile.Name ?? "Server";
        EditorEndpoint.Text = card?.Session.Profile.Endpoint ?? "";
        EditorUsername.Text = card?.Session.Profile.Username ?? "";
        EditorPassword.Clear();
        EditorAutoLogin.IsChecked = card?.Session.Profile.AutoLogin ?? true;
        EditorPasswordLabel.Text = UiText.Get(card is null ? "RCON / Layer password" : "RCON / Layer password (leave blank to keep)");
        EditorError.Text = "";
        HomePage.Visibility = WorkspacePage.Visibility = Visibility.Collapsed;
        AppSettingsPage.Visibility = Visibility.Collapsed;
        ServerEditorPage.Visibility = Visibility.Visible;
        EditorEndpoint.Focus();
    }
    private void CancelServerEditor_Click(object sender, RoutedEventArgs e)
    {
        EditorPassword.Clear(); editingCard = null; ServerEditorPage.Visibility = Visibility.Collapsed;
        if (returnToWorkspace && selected is not null) WorkspacePage.Visibility = Visibility.Visible;
        else HomePage.Visibility = Visibility.Visible;
    }
    private void EditorUsername_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (EditorUsernamePlaceholder is not null) EditorUsernamePlaceholder.Visibility = EditorUsername.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private bool savingConnection;
    private async void SaveServerEditor_Click(object sender, RoutedEventArgs e)
    {
        if (closing || savingConnection) return;
        EditorSaveButton.IsEnabled = EditorCancelButton.IsEnabled = false;
        try
        {
            await SaveConnectionAsync(editingCard, EditorName.Text, EditorEndpoint.Text, EditorUsername.Text, EditorPassword.Password, EditorAutoLogin.IsChecked == true);
            EditorPassword.Clear();
        }
        catch (Exception ex) { if (!closing) EditorError.Text = UiText.Get(ex.Message); }
        finally { EditorSaveButton.IsEnabled = EditorCancelButton.IsEnabled = true; }
    }
    private async Task SaveConnectionAsync(ServerCard? card, string name, string address, string username, string password, bool autoLogin)
    {
        if (savingConnection) return;
        var endpoint = ServerProfile.ParseEndpoint(address);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(UiText.Get("Enter a server name."));
        if (password.Length == 0 && card is null) throw new ArgumentException(UiText.Get("Enter a password."));
        SaveClient();
        savingConnection = true;
        try
        {
            var original = card?.Session.Profile;
            ServerProfile profile;
            if (original is null) profile = new();
            else lock (original.Sync) profile = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(original), ProfileStore.Json)!;
            var identityChanged = original is not null && (endpoint.Host != original.Host || endpoint.Port != original.Port || username.Trim() != original.Username);
            profile.Name = name.Trim(); profile.Host = endpoint.Host; profile.Port = endpoint.Port;
            profile.Username = username.Trim(); profile.AutoLogin = autoLogin;
            if (password.Length > 0) profile.ProtectedPassword = SecretProtection.Protect(password);
            if (identityChanged) { profile.DesiredSettings.Clear(); profile.DraftSettings.Clear(); profile.DesiredMaps = null; }
            if (card is not null)
            {
                await card.Session.DisposeAsync();
                if (closing) return;
                store.State.Servers[store.State.Servers.IndexOf(card.Session.Profile)] = profile;
                if (selected == card.Session) selected = null;
                var replacement = CreateCard(profile); cards[cards.IndexOf(card)] = replacement; card = replacement;
            }
            else { store.State.Servers.Add(profile); card = CreateCard(profile); cards.Add(card); }
            store.Save(); editingCard = null; ServerEditorPage.Visibility = Visibility.Collapsed;
            SelectServer(card.Session);
            if (profile.Layer.Enabled) await card.Session.StartLayerAsync();
            if (!closing) await card.Session.StartAsync();
        }
        finally { savingConnection = false; }
    }
}
