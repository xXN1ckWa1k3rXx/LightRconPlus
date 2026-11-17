using System.Windows;
using System.Windows.Controls;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    public static readonly DependencyProperty IsRailExpandedProperty = DependencyProperty.Register(nameof(IsRailExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsRailExpanded { get => (bool)GetValue(IsRailExpandedProperty); private set => SetValue(IsRailExpandedProperty, value); }
    private readonly System.Windows.Threading.DispatcherTimer railHover = new() { Interval = TimeSpan.FromSeconds(1) };
    private void Rail_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsActive || closing || IsRailExpanded) return;
        railHover.Stop(); railHover.Tick -= RailHover_Tick; railHover.Tick += RailHover_Tick; railHover.Start();
    }
    private void RailHover_Tick(object? sender, EventArgs e) { railHover.Stop(); if (IsActive && !closing) IsRailExpanded = true; }
    private void CollapseRail() { railHover.Stop(); IsRailExpanded = false; }
    private void Rail_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => CollapseRail();
    private void Window_Deactivated(object sender, EventArgs e) => CollapseRail();
    private void AppSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveClient(); store.Save(); EditorPassword.Clear(); editingCard = null;
        HomePage.Visibility = WorkspacePage.Visibility = ServerEditorPage.Visibility = Visibility.Collapsed;
        AppSettingsPage.Visibility = Visibility.Visible;
        switching = true; SidebarServers.SelectedItem = null; switching = false;
    }
    private void AppSettingsNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppGeneralPage is null) return;
        AppGeneralPage.Visibility = AppSettingsNavigation.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppAboutPage.Visibility = AppSettingsNavigation.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void EditCurrentConnection_Click(object sender, RoutedEventArgs e)
    {
        var card = cards.FirstOrDefault(x => x.Session == selected);
        if (card is not null) ShowServerEditor(card);
    }
    private ConnectionState? displayedConnectionState;
    private ServerSession? displayedConnectionSession;
    private ServerSession? offlineFormSession;
    private void UpdateConnectionLayout(ServerSession session)
    {
        OfflinePanel.Visibility = session.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        MainTabs.Visibility = session.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        OfflineTitle.Text = UiText.Format("{0} — {1} is not connected", session.Profile.Name, session.Profile.Endpoint);
        OfflineReason.Text = session.LastError.Length > 0 ? UiText.Format("Reason: {0}", session.LastError) : session.Status;
        OfflineRetry.Text = session.RetryAt is { } at ? UiText.Format("Retry in {0}s", Math.Max(0, (int)(at - DateTimeOffset.Now).TotalSeconds)) : "";
        OfflineConnectButton.IsEnabled = session.State == ConnectionState.Offline;
        if (!session.IsConnected && offlineFormSession != session) PopulateOfflineForm(session);
        if (session.IsConnected) { offlineFormSession = null; OfflinePassword.Clear(); }
        var admin = !session.Profile.IsLayer || session.IsConnected && session.Permissions.HasFlag(LayerPermission.LayerAdmin);
        LayersTab.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        LayerListenerPanel.Visibility = session.Profile.IsLayer ? Visibility.Collapsed : Visibility.Visible;
        if (!admin && MainTabs.SelectedItem == LayersTab) MainTabs.SelectedIndex = 0;
        if (displayedConnectionSession != session || displayedConnectionState != session.State)
        {
            displayedConnectionSession = session; displayedConnectionState = session.State;
            if (admin) _ = LoadLayerAccountsAsync(false);
        }
    }
    private void PopulateOfflineForm(ServerSession session)
    {
        offlineFormSession = session; var p = session.Profile;
        OfflineName.Text = p.Name; OfflineEndpoint.Text = p.Endpoint; OfflineUsername.Text = p.Username;
        OfflinePassword.Clear(); OfflinePasswordLabel.Text = UiText.Get("RCON / Layer password (leave blank to keep)");
        OfflineAutoLogin.IsChecked = p.AutoLogin; OfflineError.Text = "";
    }
    private void OfflineUsername_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OfflineUsernamePlaceholder is not null) OfflineUsernamePlaceholder.Visibility = OfflineUsername.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ResetOfflineConnection_Click(object sender, RoutedEventArgs e) { if (selected is not null) PopulateOfflineForm(selected); }
    private async void SaveOfflineConnection_Click(object sender, RoutedEventArgs e)
    {
        if (closing || selected is null) return;
        var card = cards.First(x => x.Session == selected);
        OfflineSaveButton.IsEnabled = OfflineCancelButton.IsEnabled = false;
        try
        {
            await SaveConnectionAsync(card, OfflineName.Text, OfflineEndpoint.Text, OfflineUsername.Text, OfflinePassword.Password, OfflineAutoLogin.IsChecked == true);
            OfflinePassword.Clear();
        }
        catch (Exception ex) { if (!closing) OfflineError.Text = UiText.Get(ex.Message); }
        finally { OfflineSaveButton.IsEnabled = OfflineCancelButton.IsEnabled = true; }
    }
}
