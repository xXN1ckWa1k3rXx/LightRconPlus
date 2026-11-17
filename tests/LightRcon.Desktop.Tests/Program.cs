global using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using LightRcon.Core;
using LightRcon.Desktop;
using LightRcon.Tests;

namespace LightRcon.Desktop.Tests;

internal static class Program
{
    private static int checks;
    private static Exception? failure;
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LightRcon.Desktop;component/Theme.xaml", UriKind.Relative) });
        app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Shutdown(1); };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await Run(); } catch (Exception ex) { failure = ex; }
            finally { app.Shutdown(failure is null ? 0 : 1); app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        }));
        Dispatcher.Run();
        if (failure is not null) { Console.Error.WriteLine(failure); return 1; }
        Console.WriteLine($"{checks} desktop checks passed."); return 0;
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }
    private static async Task Wait(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(30, timeout.Token);
    }
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static void Click(MainWindow window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task RailPointer(MainWindow window, bool enter, int? wait = null)
    {
        Control<Border>(window, "SideRail").RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
        await Task.Delay(wait ?? (enter ? 1250 : 200));
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
    }
    private static void PointerDown(UIElement source) => source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static void Tab(MainWindow window, string tag)
    {
        var tabs = Control<TabControl>(window, "MainTabs");
        tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(x => (string)x.Tag == tag);
    }
    private static async Task Close(MainWindow window, bool twice = false)
    {
        var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult();
        window.Close(); if (twice) window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "LightRcon-desktop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Check(new AppState().Language == "en", "new and legacy profiles default to English");
            var originalCulture = CultureInfo.CurrentCulture;
            foreach (var language in new[] { "en", "zh", "de" })
            {
                UiText.SetLanguage(language);
                foreach (var entry in UiText.Translations)
                {
                    if (!entry.Value.TryGetValue(language, out var text) || string.IsNullOrWhiteSpace(text)) throw new Exception("Missing translation: " + entry.Key);
                    var expected = System.Text.RegularExpressions.Regex.Matches(entry.Value["en"], @"\{\d+\}").Select(x => x.Value).Order().ToArray();
                    var actual = System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(x => x.Value).Order().ToArray();
                    if (!actual.SequenceEqual(expected)) throw new Exception("Translation placeholders differ: " + entry.Key);
                    _ = string.Format(text, Enumerable.Range(0, 8).Select(x => (object)x).ToArray());
                }
                Check(ReferenceEquals(CultureInfo.CurrentCulture, originalCulture), language + " keeps OS date and number preferences");
            }
            Check(UiText.Translations.Count > 300, "complete EN/ZH/DE catalog and matching format placeholders");
            var chatLine = new ActivityLine { Entry = new(1, DateTimeOffset.Now, "Chat", "U_AEK971", "all", "ID_CHAT_ATTACK/DEFEND") };
            var killLine = new ActivityLine { Entry = new(2, DateTimeOffset.Now, "Kill", "ID_CHAT_THANKS", "", "legacy text") { TargetName = "U_M416", Weapon = "U_AEK971", ActorTeam = 1, TargetTeam = 2 } };
            foreach (var line in new[] { chatLine, killLine })
                BindingOperations.SetBinding(line, ActivityLine.LanguageVersionProperty, new Binding(nameof(LocalizationView.Version)) { Source = LocalizationView.Instance });
            foreach (var language in new[] { "en", "zh", "de" })
            {
                UiText.SetLanguage(language);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
                var chatRuns = chatLine.Inlines.OfType<System.Windows.Documents.Run>().ToArray();
                var killRuns = killLine.Inlines.OfType<System.Windows.Documents.Run>().ToArray();
                Check(chatRuns.Any(x => x.Text == UiText.Get("ID_CHAT_ATTACK_DEFEND") && x.ToolTip as string == "ID_CHAT_ATTACK/DEFEND") && chatRuns.Any(x => x.Text == "U_AEK971"), language + " WPF chat rendering resolves the slash-form Commo Rose ID with raw-ID tooltip and unchanged player name");
                Check(killRuns.Any(x => x.Text.Contains("AEK-971") && x.ToolTip as string == "U_AEK971") && killRuns.Any(x => x.Text == "ID_CHAT_THANKS") && killRuns.Any(x => x.Text == "U_M416"), language + " WPF kill rendering resolves only the weapon and preserves both player names");
            }
            var empty = new MainWindow(Path.Combine(root, "empty")); empty.Show();
            await Close(empty, true);
            Check(!empty.IsVisible, "empty window closes, including repeated X, without reentrant Close");
            await using var mock = new MockFrostbiteServer();
            var data = Path.Combine(root, "active");
            var window = new MainWindow(data); window.Show(); await Task.Delay(60);
            Check(UiText.Language == "en", "fresh window resets to default English");
            var selector = Control<ComboBox>(window, "ServerSelector");
            selector.SelectedIndex = selector.Items.Count - 1;
            Check(Control<Grid>(window, "ServerEditorPage").IsVisible && Application.Current.Windows.Count == 1, "Add server is embedded in the same window");
            Control<PasswordBox>(window, "EditorPassword").Password = "discard me";
            Click(window, "EditorCancelButton");
            Check(Control<Grid>(window, "HomePage").IsVisible && Control<PasswordBox>(window, "EditorPassword").Password.Length == 0 && new ProfileStore(data).State.Servers.Count == 0, "cancel clears secret and does not create an instance");
            selector.SelectedIndex = -1; selector.SelectedIndex = selector.Items.Count - 1;
            Click(window, "EditorSaveButton");
            Check(Control<TextBlock>(window, "EditorError").Text.Length > 0 && Application.Current.Windows.Count == 1, "invalid instance reports an inline error");
            Control<TextBox>(window, "EditorName").Text = "Local test";
            Control<TextBox>(window, "EditorEndpoint").Text = "127.0.0.1:" + mock.Port;
            Control<PasswordBox>(window, "EditorPassword").Password = MockFrostbiteServer.Password;
            Click(window, "EditorSaveButton");
            var grid = Control<ListBox>(window, "ServerCards");
            await Wait(() => grid.Items.Count == 1 && ((ServerCard)grid.Items[0]).Session.IsConnected);
            var session = ((ServerCard)grid.Items[0]).Session;
            var serverCard = (ServerCard)grid.Items[0];
            Check(serverCard.WelcomeTitle == $"Local test | {session.Info.Name} [{session.Profile.Endpoint}]" && serverCard.SidebarLabel == session.Info.Name, "Welcome includes instance and server names while the sidebar shows only the server name");
            Check(Control<Grid>(window, "WorkspacePage").IsVisible && Application.Current.Windows.Count == 1, "saving instance connects and opens management inline");
            Check(window.WindowStyle != WindowStyle.None && window.ResizeMode == ResizeMode.CanResize, "normal resizable Windows frame is preserved");
            window.Activate(); await Wait(() => window.IsActive);
            var rail = Control<Border>(window, "SideRail");
            var railHome = Control<Button>(window, "HomeButton");
            var railSettings = Control<Button>(window, "AppSettingsButton");
            await RailPointer(window, true, 400);
            Check(!window.IsRailExpanded && rail.ActualWidth == 52, "brief sidebar hover does not expand before one second");
            await RailPointer(window, false);
            await Task.Delay(750);
            Check(!window.IsRailExpanded, "leaving during the hover delay cancels expansion");
            await RailPointer(window, true);
            Check(window.IsRailExpanded && rail.ActualWidth == 320 && Visuals<TextBlock>(railHome).Any(x => x.Text == UiText.Get("Welcome") && x.IsVisible && x.ActualWidth > 20), "pointer entry expands the sidebar and its Welcome label together");
            await Wait(() => Visuals<TextBlock>(Control<ListBox>(window, "SidebarServers")).Any(x => x.Text == session.Info.Name));
            Check(Visuals<TextBlock>(Control<ListBox>(window, "SidebarServers")).Any(x => x.Text == session.Info.Name && x.IsVisible && x.ActualWidth > 20), "expanded sidebar displays the actual server name");
            var homeIcon = Visuals<TextBlock>(railHome).First(x => x.FontFamily.Source == "Segoe MDL2 Assets");
            Check(homeIcon.TranslatePoint(new Point(), rail).X < 35, "expanded sidebar keeps its navigation icons aligned to the left");
            railSettings.Focus(); Click(window, "AppSettingsButton");
            await RailPointer(window, false);
            Check(rail.IsKeyboardFocusWithin && !window.IsRailExpanded && rail.ActualWidth == 52 && Visuals<TextBlock>(railSettings).First(x => x.Text == UiText.Get("Settings")).Visibility == Visibility.Collapsed, "leaving after clicking Settings collapses width and labels even with keyboard focus inside");
            for (var repeat = 0; repeat < 3; repeat++) { await RailPointer(window, true); await RailPointer(window, false); }
            Check(rail.ActualWidth == 52 && !window.IsRailExpanded, "repeated pointer entry and exit never leaves the sidebar expanded");
            await RailPointer(window, true, 0);
            await Wait(() => window.IsRailExpanded);
            await Task.Delay(30); window.UpdateLayout();
            Check(rail.ActualWidth > 52 && rail.ActualWidth < 320, "sidebar expansion animates between its collapsed and expanded widths");
            await Task.Delay(180); await RailPointer(window, false);
            await RailPointer(window, true);
            var focusProbe = new Window { Width = 100, Height = 100, ShowInTaskbar = false };
            focusProbe.Show(); focusProbe.Activate(); await Wait(() => !window.IsActive);
            await Task.Delay(200);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            Check(!window.IsRailExpanded && rail.ActualWidth == 52, "window deactivation collapses an expanded sidebar");
            focusProbe.Close(); window.Activate(); await Wait(() => window.IsActive);
            Check(!window.IsRailExpanded && rail.ActualWidth == 52, "reactivation does not restore a stale expanded sidebar");
            selector.SelectedIndex = -1; selector.SelectedIndex = 0;
            var theme = Control<ComboBox>(window, "ThemeSelector");
            theme.SelectedValue = "dark";
            Check(window.IsDarkTheme && ((SolidColorBrush)window.FindResource("Background")).Color.R < 64, "dark theme applies to native WPF surfaces");
            theme.SelectedValue = "light";
            Check(!window.IsDarkTheme && ((SolidColorBrush)window.FindResource("Background")).Color.R > 200, "light theme applies immediately");
            theme.SelectedValue = "dark";
            var playersGrid = Control<DataGrid>(window, "PlayersGrid");
            await Wait(() => playersGrid.Items.Count == 3);
            Check(((ListCollectionView)playersGrid.ItemsSource).Groups!.Count == 2 && playersGrid.Items.Count == 3, "team grouping preserves all players");
            Check(playersGrid.Items.Cast<PlayerInfo>().Where(x => x.Team == 1).All(x => x.TeamName == "Russian Army" && x.TeamTickets == 700) && playersGrid.Items.Cast<PlayerInfo>().Where(x => x.Team == 2).All(x => x.TeamName == "US Army" && x.TeamTickets == 650), "player groups receive the current map factions and decimal tickets");
            var fourTeams = new ListCollectionView(Enumerable.Range(1, 4).Select(i => new PlayerInfo { Name = "P" + i, Team = i }).ToArray());
            fourTeams.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlayerInfo.Team)));
            Check(fourTeams.Groups!.Count == 4, "the grouped player view handles four teams");
            playersGrid.SelectedIndex = 0; window.UpdateLayout();
            var firstPlayer = (PlayerInfo)playersGrid.SelectedItem;
            var playerRow = Visuals<DataGridRow>(playersGrid).First(x => x.Item == firstPlayer);
            playerRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Check(playersGrid.SelectedItem == firstPlayer && playersGrid.ContextMenu is { Items.Count: 4 }, "right-click selects the clicked player and builds the player menu");
            var playerMenu = playersGrid.ContextMenu!;
            var moveMenu = (MenuItem)playerMenu.Items[0];
            var teamItem = moveMenu.Items.Cast<MenuItem>().First(x => x.IsEnabled);
            teamItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Wait(() => mock.Received.Any(x => x.Length == 5 && x[0] == "admin.movePlayer" && x[1] == firstPlayer.Name));
            Check(moveMenu.Items.Count == 2 && moveMenu.Items.Cast<MenuItem>().Count(x => !x.IsEnabled) == 1 && ((MenuItem)moveMenu.Items[0]).Header.ToString() == "Russian Army", "move menu names map factions, disables the current team and sends the captured player");
            ((MenuItem)playerMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Wait(() => mock.Received.Any(x => x.SequenceEqual(new[] { "admin.killPlayer", firstPlayer.Name })));
            Check(((MenuItem)playerMenu.Items[3]).Items.Cast<MenuItem>().Select(x => x.Header.ToString()).SequenceEqual(new[] { "Battlelog", "BF4DB" }), "profile submenu contains Battlelog and BF4DB and kill sends its RCON command");
            var viewerMenu = window.BuildPlayerMenu(new ServerSession(new ServerProfile { Game = GameId.BF3 }, new ProfileStore(Path.Combine(root, "menu-only"))), firstPlayer);
            Check(!((MenuItem)viewerMenu.Items[0]).IsEnabled && !((MenuItem)viewerMenu.Items[1]).IsEnabled && !((MenuItem)((MenuItem)viewerMenu.Items[3]).Items[1]).IsEnabled, "offline actions and non-BF4 BF4DB links are disabled");
            await mock.EmitAsync("player.onKill", "Example_Bravo", firstPlayer.Name, "U_M416", "false");
            await Wait(() => playersGrid.Items.Cast<PlayerInfo>().First(x => x.Name == firstPlayer.Name).IsAlive == false);
            await Task.Delay(500); window.UpdateLayout();
            playerRow = Visuals<DataGridRow>(playersGrid).First(x => ((PlayerInfo)x.Item).Name == firstPlayer.Name);
            var deadBrush = (SolidColorBrush)window.FindResource("DeadPlayer");
            Check(((SolidColorBrush)playerRow.Background).Color == deadBrush.Color && Visuals<DataGridCell>(playerRow).All(x => ((SolidColorBrush)x.Background).Color == deadBrush.Color), "dead player row and cells use the red background even while selected in dark mode");
            theme.SelectedValue = "light";
            Check(((SolidColorBrush)playerRow.Background).Color == ((SolidColorBrush)window.FindResource("DeadPlayer")).Color && ((SolidColorBrush)playerRow.Background).Color.R > 200, "dead-row background follows the light palette");
            theme.SelectedValue = "dark";
            await mock.EmitAsync("player.onSpawn", firstPlayer.Name, "1");
            await Wait(() => playersGrid.Items.Cast<PlayerInfo>().First(x => x.Name == firstPlayer.Name).IsAlive == true);
            await Task.Delay(500); window.UpdateLayout();
            firstPlayer = playersGrid.Items.Cast<PlayerInfo>().First(x => x.Name == firstPlayer.Name);
            playersGrid.SelectedItem = firstPlayer;
            playerRow = Visuals<DataGridRow>(playersGrid).First(x => x.Item == firstPlayer);
            Check(playerRow.Background is not SolidColorBrush background || background.Color != ((SolidColorBrush)window.FindResource("DeadPlayer")).Color, "spawn clears the red player background");
            PointerDown(playerRow);
            Check(playersGrid.SelectedItem == firstPlayer && Control<Border>(window, "PlayerActionPanel").IsVisible, "clicking a player row retains selection and actions");
            PointerDown(Visuals<System.Windows.Controls.Primitives.DataGridColumnHeader>(playersGrid).First(x => x.Column is not null));
            Check(playersGrid.SelectedItem == firstPlayer, "clicking column headers does not clear the selected player");
            PointerDown(Control<TextBox>(window, "PlayerReason"));
            Check(playersGrid.SelectedItem == firstPlayer, "clicking the player action form preserves its selected player");
            PointerDown(playersGrid);
            Check(playersGrid.SelectedItem is null && Control<Border>(window, "PlayerActionPanel").Visibility == Visibility.Collapsed, "clicking empty player-list space clears selection and hides actions");
            await session.RefreshAsync(false); await Task.Delay(600);
            Check(playersGrid.SelectedItem is null && Control<Border>(window, "PlayerActionPanel").Visibility == Visibility.Collapsed, "polling does not restore a player deselected by an empty-space click");
            playersGrid.SelectedIndex = 0; window.UpdateLayout();
            Check(Control<ComboBox>(window, "BanDurationUnit").Items.Count == 5 && Visuals<Label>(Control<Border>(window, "PlayerActionPanel")).Count(x => x.Target is not null) == 3, "player action fields have labels and five duration units");
            Check(MainWindow.BanDurationMinutes("2", "hours") == 120 && MainWindow.BanDurationMinutes("2", "days") == 2880 && MainWindow.BanDurationMinutes("2", "weeks") == 20160 && MainWindow.BanDurationMinutes("2", "months") == 86400, "ban duration units convert consistently, with 30-day months");
            foreach (var invalid in new[] { "0", "-1", "1.5", "2147483647" })
            {
                try { MainWindow.BanDurationMinutes(invalid, "months"); throw new Exception("Invalid duration accepted: " + invalid); }
                catch (ArgumentException) { }
            }
            Check(true, "invalid or overflowing ban durations are rejected");
            await Wait(() => session.PlayerSnapshot()[0].PbSlot.Length > 0);
            var banPlayer = session.PlayerSnapshot()[0];
            await MainWindow.ExecutePlayerBanAsync(session, banPlayer, "Server", false, MainWindow.BanDurationMinutes("2", "days"), "Unit test reason");
            Check(mock.Received.Any(x => x.SequenceEqual(new[] { "banList.add", "guid", banPlayer.EaGuid, "seconds", "172800", "Unit test reason" })), "server temporary ban sends the selected days as seconds");
            await MainWindow.ExecutePlayerBanAsync(session, banPlayer, "PunkBuster", false, MainWindow.BanDurationMinutes("3", "weeks"), "PB test reason");
            Check(mock.Received.Any(x => x.SequenceEqual(new[] { "punkBuster.pb_sv_command", "pb_sv_kick " + banPlayer.PbSlot + " 30240 \"PB test reason\"" })), "PunkBuster temporary ban sends the selected weeks as minutes");
            await MainWindow.ExecutePlayerBanAsync(session, banPlayer, "Server", true, 0, "Permanent test");
            Check(mock.Received.Any(x => x.SequenceEqual(new[] { "banList.add", "guid", banPlayer.EaGuid, "perm", "Permanent test" })), "permanent bans do not depend on temporary duration input");
            Tab(window, "Server Settings");
            var editors = Field<Dictionary<string, System.Windows.Controls.Control>>(window, "settingEditors");
            Check(editors.Values.OfType<TextBox>().All(x => x.TextAlignment == TextAlignment.Left && x.VerticalContentAlignment == VerticalAlignment.Top && x.HorizontalContentAlignment == HorizontalAlignment.Left), "General inputs align top-left");
            Check(SettingsCatalog.All.Where(x => x.Kind == "bool").All(x => editors[x.Command] is CheckBox), "all boolean server settings use checkboxes");
            Check(SettingsCatalog.All.Where(x => x.Kind == "number" && x.Command != "vars.gameModeCounter").All(x => editors[x.Command] is NumberInput), "numeric server settings use number inputs with step controls");
            var applyButtons = Field<Dictionary<string, Button>>(window, "settingApplyButtons");
            Check(SettingsCatalog.All.Where(x => x.Kind == "bool").All(x => !applyButtons.ContainsKey(x.Command)), "standalone boolean settings have no Apply button");
            Check(!mock.Received.Any(x => x.Length > 1 && SettingsCatalog.Find(x[0])?.Kind == "bool"), "populating checkbox values does not send setting changes");
            var presetEditor = (PresetInput)editors["vars.preset"];
            var weaponEditor = (SettingChoiceBox)editors["vars.gunMasterWeaponsPreset"];
            var unlockEditor = (SettingChoiceBox)editors["vars.unlockMode"];
            Check(presetEditor.Preset.Items.Count == 5 && weaponEditor.Items.Count == 5 && unlockEditor.Items.Count == 4, "preset and unlock dropdowns offer the requested options");
            presetEditor.Preset.SelectedValue = "CLASSIC"; presetEditor.Locked.IsChecked = true;
            applyButtons["vars.preset"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.PresetLocked && mock.Settings["vars.preset"] == "CLASSIC" && presetEditor.Tag is false);
            Check(mock.Received.Any(x => x.SequenceEqual(new[] { "vars.preset", "CLASSIC", "true" })), "per-row Apply submits the selected server preset and lock flag");
            weaponEditor.SelectedValue = "4"; unlockEditor.SelectedValue = "common";
            applyButtons["vars.gunMasterWeaponsPreset"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.Settings["vars.gunMasterWeaponsPreset"] == "4" && weaponEditor.Tag is false);
            Check(mock.Settings["vars.unlockMode"] == "stats" && unlockEditor.Tag is true, "per-row Apply leaves other unsent setting edits intact");
            var friendly = (CheckBox)editors["vars.friendlyFire"]; friendly.IsChecked = false;
            friendly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.Settings["vars.friendlyFire"] == "false" && friendly.Tag is false);
            Check(mock.Received.Any(x => x.SequenceEqual(new[] { "vars.friendlyFire", "false" })), "clicking a checkbox immediately sends its invariant boolean value");
            Check(mock.Settings["vars.unlockMode"] == "stats" && unlockEditor.Value == "common" && unlockEditor.Tag is true, "automatic checkbox sync preserves other pending choice edits");
            friendly.IsChecked = true; friendly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            friendly.IsChecked = false; friendly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.Settings["vars.friendlyFire"] == "false" && friendly.Tag is false);
            await Task.Delay(150);
            Check(mock.Settings["vars.friendlyFire"] == "false" && friendly.IsChecked is false, "rapid checkbox clicks retain the latest local value");
            applyButtons["vars.unlockMode"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.Settings["vars.unlockMode"] == "common" && unlockEditor.Tag is false);
            var bullet = (NumberInput)editors["vars.bulletDamage"];
            bullet.Text = "100"; bullet.Increment(1);
            Check(bullet.Text == "101" && bullet.Tag is true, "numeric stepper updates the numeric setting draft");
            var counterEditor = (TicketCounterInput)editors["vars.gameModeCounter"];
            Check(counterEditor.BaseTickets == 800, "Conquest Large ticket preview starts with base 800");
            counterEditor.Text = "150";
            Check(counterEditor.Preview.Text.Contains("1200") && counterEditor.PercentageSlider.Value == 150, "150 percent previews 1200 tickets with the slider following the cvar");
            counterEditor.PercentageSlider.Value = 200;
            Check(counterEditor.Text == "200" && mock.Settings["vars.gameModeCounter"] == "100", "ticket slider updates the percent draft without sending commands");
            applyButtons["vars.gameModeCounter"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.Settings["vars.gameModeCounter"] == "200" && counterEditor.Tag is false);
            Check(mock.Received.Any(x => x.SequenceEqual(new[] { "vars.gameModeCounter", "200" })), "ticket Apply sends the percentage rather than total tickets");
            mock.GameMode = "RushLarge0"; await session.RefreshAsync(false);
            await Wait(() => counterEditor.BaseTickets == 100);
            counterEditor.Text = "112.5";
            Check(counterEditor.Preview.Text.Contains(112.5m.ToString("0.##", CultureInfo.CurrentCulture)), "mode-based estimates retain fractional results from the cvar percentage");
            mock.GameMode = "GunMaster0"; await session.RefreshAsync(false);
            await Wait(() => counterEditor.BaseTickets is null);
            Check(counterEditor.Preview.Text.Contains("—") && counterEditor.PercentageSlider.IsEnabled && counterEditor.Text == "112.5", "unknown ticket modes leave the cvar editable without inventing a ticket estimate");
            counterEditor.PercentageSlider.Value = 175;
            applyButtons["vars.gameModeCounter"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => mock.Settings["vars.gameModeCounter"] == "175" && counterEditor.Tag is false);
            Check(true, "unknown preview modes still apply the authoritative cvar percentage");
            mock.GameMode = "ConquestLarge0"; await session.RefreshAsync(false);
            await Wait(() => counterEditor.BaseTickets == 800);
            Check(counterEditor.Text == "175" && counterEditor.Preview.Text.Contains("1400"), "switching modes changes only the estimate and preserves the cvar percentage");
            var nameEditor = (TextBox)editors["vars.serverName"];
            nameEditor.Text = "Pending startup name";
            var settingsTabs = Control<TabControl>(window, "ServerSettingsTabs");
            settingsTabs.SelectedIndex = 1;
            var startup = Control<TextBox>(window, "StartupConfigText").Text;
            Check(startup.Contains("Pending startup name") && !startup.Contains("Map rotation") && !startup.Contains("mapList."), "Startup config appears immediately with drafts and without map rotation comments");
            settingsTabs.SelectedIndex = 0; window.UpdateLayout(); await Task.Delay(80);
            var outline = Control<ListBox>(window, "SettingsNavigation");
            var scroll = Control<ScrollViewer>(window, "SettingsScroll");
            scroll.ScrollToEnd(); await Task.Delay(80);
            Check(outline.SelectedIndex == outline.Items.Count - 1, "settings outline follows scrolling to the last section");
            outline.SelectedIndex = 0; await Task.Delay(80);
            Check(scroll.VerticalOffset < 25, "outline navigation scrolls back to the first section");
            Check(Control<TabItem>(window, "LayersTab").Visibility == Visibility.Visible && Control<Border>(window, "LayerListenerPanel").Visibility == Visibility.Visible, "direct connection exposes listener and account management");
            Tab(window, "Map List"); window.UpdateLayout();
            var mapsList = Control<ItemsControl>(window, "AvailableMapsList");
            var mapExpander = Visuals<Expander>(mapsList).First(); mapExpander.IsExpanded = true;
            window.UpdateLayout(); await Task.Delay(80);
            var roundSelectors = Visuals<ComboBox>(mapExpander).Take(2).ToArray();
            roundSelectors[0].SelectedItem = 7; roundSelectors[1].SelectedItem = 4;
            var mapButtons = Visuals<Button>(mapExpander).Where(x => x.DataContext is ModeChoice).Take(2).ToArray();
            var modeOne = (ModeChoice)mapButtons[0].DataContext; var modeTwo = (ModeChoice)mapButtons[1].DataContext;
            Check(modeOne.Rounds == 7 && modeTwo.Rounds == 4, "round selections update each mode before clicking Add");
            mapButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); mapButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => session.Profile.DesiredMaps?.Count == 4 && session.MapSnapshot().Length == 4);
            Check(session.MapSnapshot()[2] == modeOne.Entry && session.MapSnapshot()[3] == modeTwo.Entry, "expanded map rows add the correct map, mode and rounds on rapid clicks");
            var currentMaps = Control<ListBox>(window, "CurrentMapsList");
            await Wait(() => currentMaps.Items.Count == 4); window.UpdateLayout();
            var currentRow = (ListBoxItem)currentMaps.ItemContainerGenerator.ContainerFromIndex(1);
            var runsBeforeDoubleClick = mock.Received.Count(x => x[0] == "mapList.runNextRound");
            currentMaps.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
            currentMaps.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent, Source = currentRow });
            await Task.Delay(60);
            Check(mock.Received.Count(x => x[0] == "mapList.runNextRound") == runsBeforeDoubleClick, "double-clicking empty space or using the right button never switches maps");
            currentMaps.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent, Source = currentRow });
            await Wait(() => mock.LastSwitchedIndex == 1 && mock.Received.Count(x => x[0] == "mapList.runNextRound") == runsBeforeDoubleClick + 1);
            Check(mock.Map == ((MapEntry)currentMaps.Items[1]).FileName && mock.GameMode == ((MapEntry)currentMaps.Items[1]).GameMode, "double-clicking a current map row immediately switches to that row's map and mode");
            var darkText = ((SolidColorBrush)mapExpander.Foreground).Color;
            Check(darkText.R > 180 && !Visuals<TreeView>(mapsList).Any(), "dark map browser uses readable themed expanders without TreeView selection chrome");
            Control<TextBox>(window, "ChatInput").Text = "unsent draft 世界";
            Control<TabControl>(window, "MainTabs").SelectedItem = Control<TabControl>(window, "MainTabs").Items.Cast<TabItem>().First(x => (string)x.Tag == "Chat");
            Control<ComboBox>(window, "DisplayBox").SelectedIndex = 1;
            var languages = Control<ComboBox>(window, "LanguageSelector");
            foreach (var language in new[] { "zh", "de", "en" })
            {
                languages.SelectedValue = language; await Task.Delay(80);
                Check(playersGrid.Columns[2].Header.ToString() == UiText.Get("Name"), language + " updates XAML bindings including column headers");
                Check(Control<TextBox>(window, "ChatInput").Text == "unsent draft 世界" && session.Profile.Client.ChatDisplay == "Yell", language + " preserves drafts and protocol choice codes");
                Check(Control<StackPanel>(window, "YellTimePanel").Visibility == Visibility.Visible, language + " retains Yell duration controls");
                Check(weaponEditor.Value == "4" && unlockEditor.Value == "common" && presetEditor.Value == "CLASSIC true", language + " preserves invariant dropdown values across localization");
            }
            Control<TextBox>(window, "ChatInput").Text = "test localized command";
            languages.SelectedValue = "de"; Click(window, "SendChatButton");
            await Wait(() => mock.Received.Any(x => x.SequenceEqual(new[] { "admin.yell", "test localized command", "10", "all" })));
            Check(true, "German labels send invariant admin.yell audience and duration");
            // Replacing an offline connection keeps client drafts and the saved credential.
            await session.StopAsync();
            await Wait(() => Control<Grid>(window, "OfflinePanel").IsVisible);
            Control<TextBox>(window, "OfflineName").Text = "Renamed offline";
            await Task.Delay(600);
            Check(Control<TextBox>(window, "OfflineName").Text == "Renamed offline", "offline edits survive periodic UI refreshes");
            Click(window, "OfflineSaveButton");
            await Wait(() => ((ServerCard)grid.Items[0]).Session != session && ((ServerCard)grid.Items[0]).Session.IsConnected);
            session = ((ServerCard)grid.Items[0]).Session;
            Check(session.Profile.Name == "Renamed offline" && SecretProtection.Unprotect(session.Profile.ProtectedPassword) == MockFrostbiteServer.Password, "offline inline save reconnects and retains unchanged credential");
            // Layer visibility is driven by authenticated upstream permissions.
            session.Profile.Layer.BindAddress = "127.0.0.1";
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
            session.Profile.Layer.Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            await session.SaveLayerAccountAsync(false, "viewer", "Viewer-test-password", true, LayerPermission.View);
            await session.SaveLayerAccountAsync(false, "admin", "Admin-test-password", true, LayerPermission.All);
            await session.StartLayerAsync();
            var layerData = Path.Combine(root, "layers"); var layerStore = new ProfileStore(layerData);
            foreach (var user in new[] { "viewer", "admin" }) layerStore.State.Servers.Add(new ServerProfile { Name = user, Host = "127.0.0.1", Port = session.Profile.Layer.Port, Username = user, AutoLogin = true, ProtectedPassword = SecretProtection.Protect(user == "viewer" ? "Viewer-test-password" : "Admin-test-password") });
            layerStore.Save(); var layerWindow = new MainWindow(layerData); layerWindow.Show();
            var layerCards = Control<ListBox>(layerWindow, "ServerCards");
            try { await Wait(() => layerCards.Items.Cast<ServerCard>().All(x => x.Session.IsConnected)); }
            catch { foreach (var card in layerCards.Items.Cast<ServerCard>()) Console.Error.WriteLine(card.Name + ": " + card.Session.State + " " + card.Session.LastError + "\n" + string.Join("\n", card.Session.ConsoleSnapshot().TakeLast(6).Select(x => x.Formatted))); throw; }
            var layerSelector = Control<ComboBox>(layerWindow, "ServerSelector"); layerSelector.SelectedIndex = 0;
            Check(layerCards.Items.Cast<ServerCard>().All(x => x.SidebarLabel == "[Layer] " + x.Session.Info.Name && x.WelcomeTitle == $"{x.Name} | [Layer] {x.Session.Info.Name} [{x.Endpoint}]" && x.ConnectionLabel.Contains("[Layer] " + x.Session.Info.Name)), "Layer names use the prefix consistently across Welcome, sidebar and server selector");
            Check(Control<TabItem>(layerWindow, "LayersTab").Visibility == Visibility.Collapsed, "Layer viewer cannot see Layers tab");
            Check(!Control<Button>(layerWindow, "LoadMapListButton").IsEnabled, "Layer viewer cannot reload MapList.txt");
            layerSelector.SelectedIndex = 1;
            Check(Control<TabItem>(layerWindow, "LayersTab").Visibility == Visibility.Visible && Control<Border>(layerWindow, "LayerListenerPanel").Visibility == Visibility.Collapsed, "Layer administrator sees upstream accounts without local listener");
            Tab(layerWindow, "Layers");
            await Wait(() => Control<DataGrid>(layerWindow, "LayerAccountsGrid").Items.Count == 2);
            Check(true, "Layer account management automatically loads upstream accounts");
            var layerAdminSession = ((ServerCard)layerCards.Items[1]).Session;
            await layerAdminSession.LoadMapsFromDiskAsync();
            await layerAdminSession.SwitchMapAsync(0, layerAdminSession.MapSnapshot());
            Check(mock.LastSwitchedIndex == 0 && layerAdminSession.MapSnapshot().SequenceEqual(mock.DiskMaps), "an authorized Layer session can load MapList.txt and switch to its exact selected row");
            await Close(layerWindow);
            // A second window changes global language; restore this window's preference.
            languages.SelectedValue = "en"; languages.SelectedValue = "de";
            await Close(window, true);
            var stored = new ProfileStore(data);
            Check(stored.State.Language == "de" && stored.State.Theme == "dark" && stored.State.Servers.Count == 1, "language, theme and instances persist on connected shutdown");
            Check(!File.Exists(Path.Combine(data, "shutdown-errors.log")), "connected shutdown disposes sessions without cleanup errors");
            var reopened = new MainWindow(data); reopened.Show(); await Task.Delay(60);
            Check(((ServerCard)Control<ListBox>(reopened, "ServerCards").Items[0]).SidebarLabel == stored.State.Servers[0].DesiredSettings["vars.serverName"], "reopened offline instances retain the saved server name");
            Check(UiText.Language == "de" && Control<ComboBox>(reopened, "LanguageSelector").SelectedValue?.ToString() == "de", "saved German language restores on launch");
            await Close(reopened);
            Check(true, "window closes while auto login is connecting");
            foreach (var game in new[] { GameId.BF3, GameId.BFHL })
            {
                var map = MapCatalog.For(game)[0];
                await using var gameMock = new MockFrostbiteServer { GameCode = game.ToString(), Map = map.FileName, GameMode = map.Modes[0].Code };
                var gameData = Path.Combine(root, "desktop-" + game);
                var gameStore = new ProfileStore(gameData);
                gameStore.State.Servers.Add(new() { Name = game.ToString(), Host = "127.0.0.1", Port = gameMock.Port, AutoLogin = true, ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password) }); gameStore.Save();
                var gameWindow = new MainWindow(gameData); gameWindow.Show();
                var gameCards = Control<ListBox>(gameWindow, "ServerCards");
                await Wait(() => ((ServerCard)gameCards.Items[0]).Session.IsConnected);
                var gameSelector = Control<ComboBox>(gameWindow, "ServerSelector"); gameSelector.SelectedIndex = -1; gameSelector.SelectedIndex = 0;
                Tab(gameWindow, "Map List"); await Task.Delay(500);
                var browser = Control<ItemsControl>(gameWindow, "AvailableMapsList");
                Check(gameWindow.Title == "LightRcon+" && browser.Items.Cast<MapGroup>().SelectMany(x => x.Maps).Any(x => x.FileName == map.FileName && x.Modes.All(m => m.Game == game)), game + " desktop map browser follows the detected game without BF4 branding");
                var gameEditors = Field<Dictionary<string, System.Windows.Controls.Control>>(gameWindow, "settingEditors");
                if (game == GameId.BF3) Check(((SettingChoiceBox)gameEditors["vars.gunMasterWeaponsPreset"]).Items.Count == 9, "BF3 desktop exposes its nine Gun Master weapon presets");
                var menu = gameWindow.BuildPlayerMenu(((ServerCard)gameCards.Items[0]).Session, new PlayerInfo { Name = "test", Team = 1 });
                Check(!((MenuItem)((MenuItem)menu.Items[3]).Items[1]).IsEnabled && ((MenuItem)((MenuItem)menu.Items[3]).Items[0]).IsEnabled, game + " offers Battlelog while disabling BF4-only profile search");
                await Close(gameWindow);
            }
        }
        finally { UiText.SetLanguage("en"); Directory.Delete(root, true); }
    }
}
