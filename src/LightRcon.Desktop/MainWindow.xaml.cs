using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow : Window
{
    private readonly ProfileStore store;
    private readonly ObservableCollection<ServerCard> cards = [];
    private readonly ObservableCollection<object> serverChoices = [];
    private sealed record CreateServerChoice { public string Name => UiText.Get("Create Connection"); public string ConnectionLabel => Name; public override string ToString() => Name; }
    private readonly Dictionary<string, Control> settingEditors = [];
    private readonly Dictionary<string, FrameworkElement> settingSections = [];
    private readonly List<MapChoice> availableMaps = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ObservableCollection<ActivityEntry> chatEntries = [];
    private readonly ObservableCollection<ConsoleEntry> consoleEntries = [];
    private ServerSession? selected;
    private long shownVersion = -1;
    private int tick;
    private bool switching, ready, closing, shutdownComplete, dirty;
    private Point dragStart;
    private object? dragItem;
    private PublicLayerOptions? displayedLayer;
    private string shownConsoleFilter = "";
    private GameId? displayedMapGame;
    public MainWindow() : this(App.DataRoot) { }
    internal MainWindow(string? dataRoot)
    {
        InitializeComponent(); store = new(dataRoot);
        UiText.SetLanguage(store.State.Language);
        LanguageSelector.SelectedValue = UiText.Language;
        InitializeAppearance();
        Width = Math.Clamp(store.State.WindowWidth, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(store.State.WindowHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        foreach (var profile in store.State.Servers) cards.Add(CreateCard(profile));
        void UpdateChoices()
        {
            var previous = switching; switching = true; serverChoices.Clear();
            foreach (var card in cards) serverChoices.Add(card);
            serverChoices.Add(new CreateServerChoice());
            ServerSelector.SelectedItem = cards.FirstOrDefault(x => x.Session == selected); switching = previous;
        }
        cards.CollectionChanged += (_, _) => UpdateChoices(); UpdateChoices();
        SidebarServers.ItemsSource = cards; ServerCards.ItemsSource = cards; ServerSelector.ItemsSource = serverChoices;
        ChatLineLimitInput.Text = store.State.ChatLineLimit.ToString();
        PluginImportPreferenceBox.SelectedValue = store.State.PluginImportPreference;
        UpdateAudience([], "All Players");
        YellTimeBox.ItemsSource = new[] { 2, 4, 6, 8, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60 }; YellTimeBox.SelectedItem = 10;
        availableMaps.AddRange(MapCatalog.All.Select(x => new MapChoice(x.FileName, x.Name, x.Modes.Select(m => new ModeChoice(x.FileName, m.Code, m.Name)).ToList())));
        UpdateMapGroups(); BuildSettings();
        foreach (var text in new[] { ChatInput, BanSearch, ConsoleInput }) text.TextChanged += (_, _) => dirty = true;
        AudienceBox.SelectionChanged += (_, _) => dirty = true; DisplayBox.SelectionChanged += (_, _) => dirty = true; YellTimeBox.SelectionChanged += (_, _) => dirty = true;
        ready = true;
        timer.Tick += (_, _) => { RefreshView(); if (++tick % 10 == 0 && dirty) { SaveClient(); store.Save(); dirty = false; } };
    }
    private ServerCard CreateCard(ServerProfile profile)
    {
        var session = new ServerSession(profile, store);
        session.SaveClientRequested += () => { if (!Dispatcher.HasShutdownStarted) Dispatcher.Invoke(() => { if (selected == session) SaveClient(); }); };
        return new(session);
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        timer.Start(); RefreshView();
        foreach (var card in cards)
        {
            if (closing) break;
            if (card.Session.Profile.Layer.Enabled) await Run(card.Session.StartLayerAsync);
            if (closing) break;
            if (card.Session.Profile.AutoLogin) await card.Session.StartAsync();
        }
    }
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        SaveClient(); store.Save(); EditorPassword.Clear(); editingCard = null; ServerEditorPage.Visibility = Visibility.Collapsed;
        AppSettingsPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible; WorkspacePage.Visibility = Visibility.Collapsed;
        switching = true; SidebarServers.SelectedItem = null; switching = false;
    }
    private void SelectServer(ServerSession session)
    {
        if (switching || !ready) return;
        SaveClient(); store.Save(); switching = true; selected = session;
        var card = cards.First(x => x.Session == session); ServerSelector.SelectedItem = card; SidebarServers.SelectedItem = card;
        EditorPassword.Clear(); editingCard = null; ServerEditorPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Collapsed; WorkspacePage.Visibility = Visibility.Visible; store.State.SelectedServer = session.Profile.Id;
        AppSettingsPage.Visibility = Visibility.Collapsed;
        var p = session.Profile; AutoLoginBox.IsChecked = p.AutoLogin;
        ChatInput.Text = p.Client.ChatDraft; BanSearch.Text = p.Client.BanSearch; ConsoleInput.Text = p.Client.ConsoleDraft;
        DisplayBox.SelectedIndex = p.Client.ChatDisplay == "Yell" ? 1 : 0; YellTimeBox.SelectedItem = p.Client.YellSeconds;
        ShowAdminSayYellBox.IsChecked = p.Client.ShowAdminSayYell;
        UpdateAudience(session.PlayerSnapshot(), p.Client.ChatAudience);
        MainTabs.SelectedItem = MainTabs.Items.Cast<TabItem>().FirstOrDefault(x => (string)x.Tag == p.Client.SelectedTab && (x != LayersTab || !p.IsLayer || session.Permissions.HasFlag(LayerPermission.LayerAdmin))) ?? MainTabs.Items[0];
        PlayersTabs.SelectedIndex = Math.Clamp(p.Client.PlayerTab, 0, 2); LayerBind.Text = p.Layer.BindAddress; LayerPort.Text = p.Layer.Port.ToString();
        RestoreGrid(PlayersGrid, "players"); RestoreGrid(BansGrid, "bans");
        chatEntries.Clear(); consoleEntries.Clear(); ChatList.Clear(); ConsoleList.Clear(); PluginConsoleList.Clear(); StartupConfigText.Clear(); PopulateSettings(true);
        switching = false; shownVersion = -1; dirty = false; UpdateMapGroups(); RefreshView(); _ = LoadLayerAccountsAsync(false);
    }
    private void SidebarServers_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!switching && SidebarServers.SelectedItem is ServerCard card) SelectServer(card.Session); }
    private void ServerSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switching) return;
        if (ServerSelector.SelectedItem is ServerCard card) SelectServer(card.Session);
        else if (ServerSelector.SelectedItem is CreateServerChoice)
        {
            switching = true; ServerSelector.SelectedItem = cards.FirstOrDefault(x => x.Session == selected); switching = false;
            CreateServer_Click(sender, e);
        }
    }
    private void OpenServer_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is ServerCard card) SelectServer(card.Session); }
    private void CreateServer_Click(object sender, RoutedEventArgs e) => ShowServerEditor(null);
    private void EditServer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServerCard card) ShowServerEditor(card);
        else if (ServerCards.SelectedItem is ServerCard selectedCard) ShowServerEditor(selectedCard);
    }
    private async void RemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServerCard card || !Confirm(UiText.Format("Remove server {0}? Connections will close. Existing chat log files will be kept.", card.Name))) return;
        await Run(async () =>
        {
            await card.Session.DisposeAsync(); if (selected == card.Session) { SaveClient(); selected = null; HomePage.Visibility = Visibility.Visible; WorkspacePage.Visibility = Visibility.Collapsed; }
            cards.Remove(card); store.State.Servers.Remove(card.Session.Profile); store.Save();
        });
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) { var s = selected; if (s is not null) await Run(s.StartAsync); }
    private async void Disconnect_Click(object sender, RoutedEventArgs e) { var s = selected; if (s is not null) await Run(s.StopAsync); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { var s = selected; if (s is not null) await Run(() => s.RefreshAsync()); }
    private void AutoLogin_Click(object sender, RoutedEventArgs e) { if (selected is null || switching) return; selected.Profile.AutoLogin = AutoLoginBox.IsChecked == true; store.Save(); }
    private void SaveClient()
    {
        if (!ready || selected is null || switching) return;
        var p = selected.Profile;
        lock (p.Sync)
        {
            p.Client.SelectedTab = (MainTabs.SelectedItem as TabItem)?.Tag?.ToString() ?? "Players"; p.Client.PlayerTab = PlayersTabs.SelectedIndex;
            p.Client.ChatDraft = ChatInput.Text; p.Client.ChatAudience = AudienceCode;
            p.Client.ChatDisplay = ComboText(DisplayBox); p.Client.YellSeconds = (int?)YellTimeBox.SelectedItem ?? 10;
            p.Client.ShowAdminSayYell = ShowAdminSayYellBox.IsChecked == true;
            p.Client.BanSearch = BanSearch.Text; p.Client.ConsoleDraft = ConsoleInput.Text;
            p.Client.Grids["players"] = SaveGrid(PlayersGrid); p.Client.Grids["bans"] = SaveGrid(BansGrid);
            foreach (var item in settingEditors) if (item.Value.Tag is true) p.DraftSettings[item.Key] = EditorValue(item.Value);
        }
        if (WindowState == WindowState.Normal) { store.State.WindowWidth = ActualWidth; store.State.WindowHeight = ActualHeight; }
    }
    private static List<GridColumnPreference> SaveGrid(DataGrid grid) => grid.Columns.Select(x => new GridColumnPreference(x.Header?.ToString() ?? "", double.IsFinite(x.ActualWidth) && x.ActualWidth >= 25 ? x.ActualWidth : x.Width.IsAbsolute ? Math.Max(25, x.Width.Value) : 140, x.DisplayIndex, x.SortDirection?.ToString())).ToList();
    private void RestoreGrid(DataGrid grid, string name)
    {
        if (selected is null || !selected.Profile.Client.Grids.TryGetValue(name, out var preferences) || preferences.Count != grid.Columns.Count) return;
        for (var i = 0; i < grid.Columns.Count; i++)
        {
            var saved = preferences[i]; var column = grid.Columns[i];
            column.Width = new DataGridLength(Math.Clamp(saved.Width, 25, 900));
            column.SortDirection = Enum.TryParse<ListSortDirection>(saved.SortDirection, out var sort) ? sort : null;
        }
        foreach (var pair in preferences.Select((value, index) => (value, index)).OrderBy(x => x.value.Order))
            grid.Columns[pair.index].DisplayIndex = Math.Clamp(pair.value.Order, 0, grid.Columns.Count - 1);
    }
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!ready || switching || e.Source != MainTabs) return; dirty = true; RefreshView(); if (MainTabs.SelectedItem == LayersTab) _ = LoadLayerAccountsAsync(false); }
    private void PlayersTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready && !switching && e.Source == PlayersTabs) dirty = true; }
    private void RefreshView()
    {
        if (closing) return;
        foreach (var card in cards) card.Refresh(); SidebarCount.Text = cards.Count.ToString();
        var online = cards.Where(x => x.Session.IsConnected).ToArray();
        HomeSummary.Text = UiText.Format("{0} servers · {1} connected · {2} players", cards.Count, online.Length, online.Sum(x => x.Session.Info.Players));
        EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed; if (selected is null) return;
        var s = selected; ServerTitle.Text = cards.First(x => x.Session == s).ServerDisplayName;
        if (displayedMapGame != s.Game) { displayedMapGame = s.Game; UpdateMapGroups(); }
        ServerSummary.Text = UiText.Format("{0} — {1}    Round: {2}/{3}    Uptime: {4}", s.Info.MapName, s.Info.ModeName, s.Info.Round, s.Info.TotalRounds, s.Info.UptimeText);
        FooterStatus.Text = s.Status + (s.RetryAt is { } retry ? UiText.Format(" · Retry in {0}s", Math.Max(0, (int)(retry - DateTimeOffset.Now).TotalSeconds)) : "") + (s.LastError.Length > 0 ? " · " + UiText.Get(s.LastError) : "");
        ConnectionBanner.Visibility = s.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        ConnectionBannerText.Text = s.ConnectionLost ? UiText.Get("Connection to the remote server has been lost. Client settings are saved; reconnecting.") : s.Status + (s.LastError.Length > 0 ? " · " + s.LastError : "");
        UpdateConnectionLayout(s);
        RefreshPluginList(s);
        ConnectButton.IsEnabled = s.State is ConnectionState.Offline or ConnectionState.RetryWaiting;
        RestartButton.IsEnabled = NextRoundButton.IsEnabled = s.IsConnected && s.Permissions.HasFlag(LayerPermission.Maps); SendChatButton.IsEnabled = s.IsConnected && s.Permissions.HasFlag(LayerPermission.Chat);
        UpdateRoundActions(s);
        LoadMapListButton.IsEnabled = s.IsConnected && s.Permissions.HasFlag(LayerPermission.Maps);
        AddManualBanButton.IsEnabled = s.IsConnected && s.Permissions.HasFlag(LayerPermission.Bans);
        ChatRetentionText.Text = UiText.Format("Latest {0} lines shown", store.State.ChatLineLimit) + " · " + UiText.Get(s.Profile.IsLayer ? "History from upstream Layer" : "Logs saved locally");
        LayerHostStatus.Text = s.IsHosting ? UiText.Format("Listening · {0} clients", s.LayerClientCount) : UiText.Get("Disabled");
        if (shownVersion == s.Version) return; shownVersion = s.Version;
        var playerName = (PlayersGrid.SelectedItem as PlayerInfo)?.Name; var players = s.PlayerSnapshot();
        foreach (var player in players)
        {
            player.TeamTickets = player.Team > 0 && player.Team <= s.Info.TeamScores.Length ? s.Info.TeamScores[player.Team - 1] : null;
            player.TeamName = TeamCatalog.Name(s.Info.Map, s.Info.GameMode, player.Team, s.Game);
        }
        var view = new ListCollectionView(players); view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlayerInfo.Team)));
        view.SortDescriptions.Add(new SortDescription(nameof(PlayerInfo.Team), ListSortDirection.Ascending));
        PlayersGrid.ItemsSource = view; PlayersGrid.SelectedItem = players.FirstOrDefault(x => x.Name == playerName);
        PlayerCountText.Text = UiText.Format("{0} players · Play time measured during this client session", players.Length);
        UpdateAudience(players, AudienceCode);
        FilterBans(); var mapIndex = CurrentMapsList.SelectedIndex;
        CurrentMapsList.ItemsSource = s.Profile.DesiredMaps?.ToArray() ?? s.MapSnapshot(); if (mapIndex < CurrentMapsList.Items.Count) CurrentMapsList.SelectedIndex = mapIndex;
        var spec = SpectatorList.SelectedItem; SpectatorList.ItemsSource = s.SpectatorSnapshot(); SpectatorList.SelectedItem = spec;
        var reserved = ReservedList.SelectedItem; ReservedList.ItemsSource = s.ReservedSnapshot(); ReservedList.SelectedItem = reserved;
        UpdateChat(); UpdateConsole(); UpdatePluginConsole(); PopulateSettings(false); UpdateStartupConfig();
    }
    private readonly Dictionary<Guid, long> clearedChat = [];
    private void UpdateChat()
    {
        if (selected is null) return;
        var limit = store.State.ChatLineLimit;
        var snapshot = selected.Journal.Snapshot().Where(entry => entry.Sequence > clearedChat.GetValueOrDefault(selected.Profile.Id))
            .Where(entry => ShowAdminSayYellBox.IsChecked == true || !IsPluginChat(entry)).TakeLast(limit).ToArray();
        chatEntries.Clear(); foreach (var entry in snapshot) chatEntries.Add(entry);
        ChatList.UpdateEntries(snapshot, limit);
    }
    private static bool IsPluginChat(ActivityEntry entry) => entry.Kind == "Chat" && entry.IsPluginMessage;
    private void ShowAdminSayYell_Click(object sender, RoutedEventArgs e)
    {
        if (!ready || switching || selected is null) return;
        SaveClient(); store.Save(); UpdateChat();
    }
    private void ClearChat_Click(object sender, RoutedEventArgs e)
    {
        if (selected is null) return;
        clearedChat[selected.Profile.Id] = selected.Journal.Snapshot().LastOrDefault()?.Sequence ?? 0;
        chatEntries.Clear(); ChatList.Clear();
    }
    private void UpdatePluginConsole()
    {
        if (selected is null) return;
        UpdateLogText(PluginConsoleList, selected.ConsoleSnapshot().Where(entry => entry.IsPluginConsole).Select(entry => entry.Formatted), 3000);
    }
    private void UpdateConsole()
    {
        if (selected is null) return;
        var filter = ComboText(ConsoleFilter);
        var entries = selected.ConsoleSnapshot().Where(entry => filter == "All" || entry.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase)).TakeLast(3000).ToArray();
        consoleEntries.Clear(); foreach (var entry in entries) consoleEntries.Add(entry);
        if (shownConsoleFilter != filter) ConsoleList.Clear();
        shownConsoleFilter = filter;
        UpdateLogText(ConsoleList, entries.Select(entry => entry.Formatted), 3000);
    }
    private static void UpdateLogText(TextBox box, IEnumerable<string> entries, int limit)
    {
        var text = string.Join("\n", entries).Replace("\r\n", "\n").Replace('\r', '\n');
        text = string.Join(Environment.NewLine, text.Split('\n').TakeLast(limit));
        if (box.Text == text) return;
        var follow = box.Text.Length == 0 || IsAtBottom(box);
        var start = box.SelectionStart; var length = box.SelectionLength; var offset = box.VerticalOffset;
        box.Text = text;
        box.Select(Math.Min(start, text.Length), Math.Min(length, Math.Max(0, text.Length - start)));
        if (follow) box.ScrollToEnd(); else box.ScrollToVerticalOffset(offset);
    }
    private void LogText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ActivityEditor chat) chat.ScrollToEnd();
        else ((TextBox)sender).ScrollToEnd();
    }
    private void ConsoleFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready) UpdateConsole(); }
    private static bool IsAtBottom(DependencyObject control) { var scroll = FindChild<ScrollViewer>(control); return scroll is null || scroll.VerticalOffset >= scroll.ScrollableHeight - 20; }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) return match; var nested = FindChild<T>(child); if (nested is not null) return nested; } return null;
    }
    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null) { if (source is T item) return item; source = source is Visual || source is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source); } return null;
    }
    private ServerSession RequireSession() => selected ?? throw new ArgumentException(UiText.Get("Select a server first."));
    private Task Command(string[] words) => Run(async () => { await RequireSession().ExecuteAsync(words); });
    private static string ComboText(ComboBox box) => box.SelectedItem is LocalizedChoice choice ? choice.Code : (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? box.SelectedItem?.ToString() ?? "";
    private bool Confirm(string text) => !closing && AppDialog.Confirm(this, text);
    private void ShowError(Exception ex) { if (closing) return; FooterStatus.Text = UiText.Get(ex.Message); AppDialog.Alert(this, UiText.Get(ex.Message)); }
    private async Task Run(Func<Task> operation) { try { await operation(); if (!closing) { shownVersion = -1; RefreshView(); } } catch (Exception ex) { ShowError(ex); } }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (shutdownComplete) return;
        e.Cancel = true;
        if (closing) return;
        Keyboard.ClearFocus();
        closing = true; timer.Stop(); CollapseRail(); IsEnabled = false;
        // The dispatcher must leave this Closing event before cleanup can finish and Close runs again.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () => await CompleteShutdownAsync()));
    }
    private async Task CompleteShutdownAsync()
    {
        List<Exception> errors = [];
        try { await pendingPluginSave; } catch (Exception error) { errors.Add(error); }
        try { SaveClient(); store.Save(); } catch (Exception ex) { errors.Add(ex); }
        try { await Task.WhenAll(cards.Select(async x => await x.Session.DisposeAsync())); } catch (Exception ex) { errors.Add(ex); }
        try { store.Save(); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0)
        {
            // Never open a dialog owned by a window during shutdown.
            try { File.AppendAllText(Path.Combine(store.Root, "shutdown-errors.log"), string.Join(Environment.NewLine, errors.Select(x => $"{DateTimeOffset.Now:O} {x}")) + Environment.NewLine); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        shutdownComplete = true;
        Close();
    }
}
