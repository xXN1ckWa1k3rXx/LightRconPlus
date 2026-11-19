global using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private static void Capture(FrameworkElement window, string name)
    {
        if (Environment.GetEnvironmentVariable("LIGHTRCON_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
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
            var pluginPath = Path.Combine(root, "DesktopPlugin.lua");
            File.WriteAllText(pluginPath, "plugin = { name = 'Desktop API', version = '3.0', author = 'UI author', description = 'Details from Lua' }; settings.define('Messages|Text', 'string', 'before'); settings.define('Messages|Active', 'bool', true); settings.define('Messages|Mode', 'enum.Mode(One|Two)', 'One'); settings.define('Info|Read-only', 'int', 5, true); settings.define('Info|Lines', 'stringarray', {'one', 'two'})");
            await session.Plugins.ImportAsync(pluginPath);
            var mainTabs = Control<TabControl>(window, "MainTabs");
            mainTabs.SelectedItem = mainTabs.Items.Cast<TabItem>().Single(tab => tab.Tag as string == "Plugin");
            await Wait(() => Control<ListBox>(window, "EnabledPluginList").Items.Count == 1 && Control<TextBlock>(window, "PluginAuthorText").Text == "UI author");
            Check(Control<TextBlock>(window, "PluginNameText").Text == "Desktop API" && Control<TextBlock>(window, "PluginVersionText").Text == "3.0" && Control<TextBlock>(window, "PluginDescriptionText").Text == "Details from Lua", "plugin Details page reads actual runtime metadata");
            window.UpdateLayout();
            Check(Control<Button>(window, "EnableSelectedPluginButton").IsVisible && !Control<Button>(window, "DisableSelectedPluginButton").IsVisible && Control<Button>(window, "EnableSelectedPluginButton").ActualHeight >= 42 && Control<Button>(window, "EnableSelectedPluginButton").TranslatePoint(new Point(), Control<TabControl>(window, "PluginPages")).Y < 0, "selected plugin has one prominent activation button above its pages");
            var pluginTab = (TabItem)mainTabs.SelectedItem;
            var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            pluginTab.SetValue(hoverKey, true);
            Check(Equals(pluginTab.Foreground, window.FindResource("Accent")), "selected tab titles retain their blue accent while their container is hovered");
            pluginTab.SetValue(hoverKey, false);
            Capture(window, "plugin-details");
            var pluginPages = Control<TabControl>(window, "PluginPages");
            pluginPages.SelectedIndex = 1;
            var pluginFields = Control<StackPanel>(window, "PluginSettingFields");
            Check(pluginFields.Children.OfType<ComboBox>().Single().Items.Count == 2 && pluginFields.Children.OfType<TextBox>().Any(box => !box.IsEnabled), "plugin Settings page renders enum choices and read-only controls");
            var pluginInput = pluginFields.Children.OfType<TextBox>().Single(box => box.Tag as string == "Messages|Text");
            Capture(window, "plugin-settings");
            pluginInput.Text = "after";
            pluginInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, pluginInput, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            await Wait(() => Control<TextBlock>(window, "PluginPageStatus").Text == UiText.Get("Plugin settings saved."));
            Check((await session.Plugins.GetSettingsAsync(pluginPath)).Single(setting => setting.Name == "Messages|Text").Value == "after", "plugin Settings page saves edited values to runtime");
            Click(window, "EnableSelectedPluginButton");
            await Wait(() => session.Plugins.Snapshot().Single().Enabled);
            await Wait(() => Control<Button>(window, "DisableSelectedPluginButton").IsVisible);
            Check(!Control<Button>(window, "EnableSelectedPluginButton").IsVisible, "enabled plugins show only the prominent disable action");
            Click(window, "DisableSelectedPluginButton");
            await Wait(() => !session.Plugins.Snapshot().Single().Enabled);
            Check(Control<ListBox>(window, "EnabledPluginList").Items.Count == 1, "disabled plugin remains selectable for Details and Settings");
            await Wait(() => Control<TabControl>(window, "PluginPages").IsEnabled);
            var draftPluginSetting = Control<StackPanel>(window, "PluginSettingFields").Children.OfType<TextBox>().Single(box => box.Tag as string == "Messages|Text");
            draftPluginSetting.Text = "unsaved plugin draft";
            foreach (var language in new[] { "zh", "de", "en" })
            {
                Control<ComboBox>(window, "LanguageSelector").SelectedValue = language;
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
                Check(Control<TextBlock>(window, "ProconBannerText").Text == UiText.Get("This plugin is designed for Procon. Compatibility is enabled; stability is not guaranteed."), language + " compatibility warning is localized");
                Check(draftPluginSetting.Text == "unsaved plugin draft", language + " localization preserves unsaved plugin settings");
            }
            if (Environment.GetEnvironmentVariable("LIGHTRCON_TEST_PROCON_ZIP") is { } package)
            {
                File.Copy(package, Path.Combine(data, "Procon-v1.5.4.2.zip"), true);
                var legacy = Path.Combine(root, "DesktopProcon.cs");
                File.WriteAllText(legacy, """
                    using System.Collections.Generic;
                    using PRoCon.Core;
                    using PRoCon.Core.Plugin;
                    namespace PRoConEvents {
                        public class DesktopProcon : PRoConPluginAPI, IPRoConPluginInterface {
                            private string text = "hello";
                            public string GetPluginName() { return "Procon desktop integration"; }
                            public string GetPluginVersion() { return "1.0"; }
                            public string GetPluginAuthor() { return "Upstream API"; }
                            public string GetPluginWebsite() { return "https://example.com"; }
                            public string GetPluginDescription() { return "<h2>Procon plugin</h2><p>Metadata and settings from the compatibility runtime.</p>"; }
                            public void OnPluginLoaded(string host, string port, string version) { }
                            public void OnPluginEnable() { }
                            public void OnPluginDisable() { }
                            public List<CPluginVariable> GetPluginVariables() { return new List<CPluginVariable> { new CPluginVariable("Message", typeof(string), text) }; }
                            public List<CPluginVariable> GetDisplayPluginVariables() { return new List<CPluginVariable> { new CPluginVariable("Messages|Message", typeof(string), text), new CPluginVariable("Messages|Mode", "enum.Mode(One|Two)", "One") }; }
                            public void SetPluginVariable(string name, string value) { if (name == "Message") text = value; }
                        }
                    }
                    """);
                await session.Plugins.EnableAsync(legacy);
                await Wait(() => Control<ListBox>(window, "EnabledPluginList").Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Any(plugin => plugin.Path == legacy));
                var list = Control<ListBox>(window, "EnabledPluginList");
                list.SelectedItem = list.Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Single(plugin => plugin.Path == legacy);
                await Wait(() => Control<TextBlock>(window, "PluginNameText").Text == "Procon desktop integration" && Control<TabControl>(window, "PluginPages").IsEnabled);
                Check(Control<TextBlock>(window, "PluginAuthorText").Text == "Upstream API" && !Control<TextBlock>(window, "PluginDescriptionText").Text.Contains("<h2>"), "Procon Details page displays runtime metadata and readable HTML description");
                pluginPages.SelectedIndex = 0;
                Capture(window, "procon-details");
                pluginPages.SelectedIndex = 1;
                Capture(window, "procon-settings");
                Check(Control<Border>(window, "ProconBanner").IsVisible && Control<Border>(window, "ProconBanner").TranslatePoint(new Point(), list).X > list.ActualWidth, "Procon warning belongs to the selected plugin content area");
                list.SelectedItem = list.Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Single(plugin => plugin.Path == pluginPath);
                await Wait(() => Control<TextBlock>(window, "PluginNameText").Text == "Desktop API" && Control<TabControl>(window, "PluginPages").IsEnabled);
                Check(!Control<Border>(window, "ProconBanner").IsVisible, "selecting Lua hides warnings for other enabled Procon plugins");
                list.SelectedItem = list.Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Single(plugin => plugin.Path == legacy);
                await Wait(() => Control<Border>(window, "ProconBanner").IsVisible);
                await session.Plugins.DisableAsync(legacy);
                await Wait(() => Control<Button>(window, "EnableSelectedPluginButton").IsVisible);
                Check(Control<Border>(window, "ProconBanner").IsVisible, "disabled Procon plugins retain their compatibility warning");
                pluginPages.SelectedIndex = 0;
                Check(Control<Border>(window, "ProconBanner").IsVisible, "Procon compatibility warning remains visible on both plugin pages");
                var broken = Path.Combine(root, "BrokenProcon.cs");
                File.WriteAllText(broken, "using PRoCon.Core.Plugin; namespace PRoConEvents { public class BrokenProcon : PRoConPluginAPI { invalid syntax } }");
                var failedImport = System.Diagnostics.Stopwatch.StartNew();
                try { await session.Plugins.ImportAsync(broken); } catch (Exception) { }
                var failureRead = System.Diagnostics.Stopwatch.StartNew();
                var failedDetails = await session.Plugins.GetDetailsAsync(broken);
                var failedSettings = await session.Plugins.GetSettingsAsync(broken);
                Check(failedImport.Elapsed < TimeSpan.FromSeconds(15) && failedDetails.Error.Contains("BrokenProcon") && !failedDetails.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase), "Procon load errors read active logs and return the actual failure without waiting for request timeout");
                Check(failedDetails.Error.Length > 0 && failedSettings.Length == 0 && failureRead.Elapsed < TimeSpan.FromSeconds(2), "reading failed plugin metadata and settings returns cached errors without recompiling");
                await Wait(() => list.Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Any(item => item.Path == broken));
                list.SelectedItem = list.Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Single(item => item.Path == broken);
                try { await Wait(() => Control<TextBlock>(window, "PluginErrorText").Text.Length > 0 && Control<TabControl>(window, "PluginPages").IsEnabled); }
                catch
                {
                    Console.Error.WriteLine($"Broken plugin selection: selected={list.SelectedItem}; pages={Control<TabControl>(window, "PluginPages").IsEnabled}; status={Control<TextBlock>(window, "PluginPageStatus").Text}; error={Control<TextBlock>(window, "PluginErrorText").Text}");
                    throw;
                }
                Check(Control<Border>(window, "ProconBanner").IsVisible && session.Plugins.Snapshot().Single(item => item.Path == broken).Kind == "Procon", "Procon compilation failures retain classification and compatibility warning");
            }
            mainTabs.SelectedIndex = 0;
            var playersTabs = Control<TabControl>(window, "PlayersTabs"); playersTabs.SelectedIndex = 1;
            window.UpdateLayout();
            var manualFields = new[] { "BanIdType", "BanIdentifier", "BanDurationType", "BanDurationValue", "ManualBanDurationUnit", "ManualBanReason" };
            Check(manualFields.All(name => Visuals<Label>(Control<Expander>(window, "ManualBanPanel")).Any(label => ReferenceEquals(label.Target, window.FindName(name)))), "manual ban form gives every input a visible associated title");
            var manualType = Control<ComboBox>(window, "BanDurationType");
            var manualUnit = Control<ComboBox>(window, "ManualBanDurationUnit");
            var manualAmount = Control<TextBox>(window, "BanDurationValue");
            Check(!manualAmount.IsEnabled && !manualUnit.IsEnabled && manualUnit.Items.Count == 6, "permanent bans disable duration inputs and all six time units are available");
            Control<ComboBox>(window, "BanIdType").SelectedIndex = 1;
            manualType.SelectedIndex = 1;
            Control<TextBox>(window, "ManualBanReason").Text = "manual form test";
            var unitSeconds = new[] { 1, 60, 3600, 86400, 604800, 2592000 };
            for (var index = 0; index < unitSeconds.Length; index++)
            {
                manualUnit.SelectedIndex = index; manualAmount.Text = "2";
                var identifier = "ManualUnit" + index;
                Control<TextBox>(window, "BanIdentifier").Text = identifier;
                Click(window, "AddManualBanButton");
                await Wait(() => mock.Received.Any(words => words.SequenceEqual(new[] { "banList.add", "name", identifier, "seconds", (2 * unitSeconds[index]).ToString(CultureInfo.InvariantCulture), "manual form test" })));
            }
            Check(true, "manual ban converts seconds, minutes, hours, days, weeks and 30-day months to protocol seconds");
            Capture(window, "manual-ban");
            manualType.SelectedIndex = 2; manualAmount.Text = "3";
            Control<TextBox>(window, "BanIdentifier").Text = "ManualRounds"; Click(window, "AddManualBanButton");
            await Wait(() => mock.Received.Any(words => words.SequenceEqual(new[] { "banList.add", "name", "ManualRounds", "rounds", "3", "manual form test" })));
            Check(manualAmount.IsEnabled && !manualUnit.IsEnabled, "round-based bans send round counts without time conversion");
            manualType.SelectedIndex = 0; manualAmount.Text = "ignored invalid duration";
            Control<TextBox>(window, "BanIdentifier").Text = "ManualPermanent"; Click(window, "AddManualBanButton");
            await Wait(() => mock.Received.Any(words => words.SequenceEqual(new[] { "banList.add", "name", "ManualPermanent", "perm", "manual form test" })));
            try { MainWindow.ManualBanSeconds(int.MaxValue.ToString(CultureInfo.InvariantCulture), "months"); throw new Exception("Overflow accepted"); }
            catch (ArgumentException) { Check(true, "manual duration rejects overflow and permanent bans ignore duration drafts"); }
            playersTabs.SelectedIndex = 0;
            Tab(window, "Chat");
            for (var index = 0; index < 100; index++) session.Journal.Add("Chat", "Scroll test", "all", "Pinned message " + index);
            var chatList = Control<TextBox>(window, "ChatList");
            await Wait(() => chatList.Text.Contains("Pinned message 99"));
            window.UpdateLayout();
            var chatScroll = Visuals<ScrollViewer>(chatList).First();
            await Wait(() => chatScroll.ScrollableHeight > 0 && chatScroll.VerticalOffset >= chatScroll.ScrollableHeight - 1);
            chatScroll.ScrollToTop();
            await Wait(() => chatScroll.VerticalOffset == 0);
            Check(chatList.IsReadOnly && chatList.AcceptsReturn, "chat uses a selectable text editor and permits scrolling to older messages");
            chatList.ScrollToEnd(); await Task.Delay(60);
            session.Journal.Add("Chat", "Scroll test", "all", "Newest pinned message");
            await Wait(() => chatList.Text.EndsWith("Newest pinned message") && chatScroll.VerticalOffset >= chatScroll.ScrollableHeight - 1);
            Tab(window, "Plugin"); Tab(window, "Chat");
            await Wait(() => chatScroll.VerticalOffset >= chatScroll.ScrollableHeight - 1);
            Check(true, "new chat messages and returning to Chat keep the newest entry visible");
            mainTabs.SelectedIndex = 0;
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
            playerMenu.PlacementTarget = playersGrid; playerMenu.IsOpen = true;
            await Task.Delay(80);
            var profileSubmenu = (MenuItem)playerMenu.Items[3]; profileSubmenu.IsSubmenuOpen = true;
            await Task.Delay(80); profileSubmenu.ApplyTemplate();
            var submenuPopup = (System.Windows.Controls.Primitives.Popup)profileSubmenu.Template.FindName("PART_Popup", profileSubmenu);
            var submenuBorder = (Border)submenuPopup.Child;
            Check(Equals(submenuBorder.Background, window.FindResource("Surface")) && Equals(profileSubmenu.Foreground, window.FindResource("Text")) && ((SolidColorBrush)submenuBorder.Background).Color.R < 100, "dark context menus theme submenu surfaces and text without system white panels");
            Capture(playerMenu, "dark-context-menu"); Capture(submenuBorder, "dark-context-submenu");
            playerMenu.IsOpen = false;
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
            Check(SettingsCatalog.For(session.Game).Where(x => x.Kind == "bool").All(x => editors[x.Command] is CheckBox), "all boolean server settings use checkboxes");
            Check(SettingsCatalog.For(session.Game).Where(x => x.Kind == "number" && x.Command != "vars.gameModeCounter").All(x => editors[x.Command] is NumberInput), "numeric server settings use number inputs with step controls");
            var applyButtons = Field<Dictionary<string, Button>>(window, "settingApplyButtons");
            Check(SettingsCatalog.All.Where(x => x.Kind == "bool").All(x => !applyButtons.ContainsKey(x.Command)), "standalone boolean settings have no Apply button");
            Check(!editors.ContainsKey("vars.hacker") && editors.ContainsKey("vars.commander") && !editors.ContainsKey("vars.ctfRoundTimeModifier"), "BF4 settings omit BF3 and Hardline controls entirely");
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
            try { await Wait(() => mock.Settings["vars.gameModeCounter"] == "175" && counterEditor.Tag is false); }
            catch
            {
                Console.Error.WriteLine($"Counter apply: remote={mock.Settings["vars.gameModeCounter"]}; text={counterEditor.Text}; tag={counterEditor.Tag}; enabled={counterEditor.IsEnabled}; state={session.State}; status={Control<TextBlock>(window, "FooterStatus").Text}");
                throw;
            }
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
            Check(Control<Button>(window, "EnableLayerButton").Visibility == Visibility.Visible && Control<Button>(window, "DisableLayerButton").Visibility == Visibility.Collapsed, "inactive listener offers only the blue enable action");
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
            await session.Plugins.EnableAsync(pluginPath);
            mock.DropClients();
            await Wait(() => session.State == ConnectionState.RetryWaiting && Control<Button>(window, "OfflineConnectButton").IsEnabled);
            Check(Control<Button>(window, "ConnectButton").IsEnabled && Control<Button>(window, "OfflineConnectButton").IsEnabled, "both manual connection buttons are enabled during retry waiting");
            var retryStarted = System.Diagnostics.Stopwatch.StartNew();
            Click(window, "OfflineConnectButton");
            await Wait(() => session.IsConnected);
            Check(retryStarted.Elapsed < TimeSpan.FromSeconds(5), "manual connection click reconnects before the twenty-second countdown");
            await Wait(() => session.Plugins.Snapshot().Any(plugin => plugin.Path == pluginPath && plugin.Enabled));
            Check((await session.Plugins.GetSettingsAsync(pluginPath)).Single(setting => setting.Name == "Messages|Text").Value == "after", "Lua enabled state and settings restore after manual reconnect");
            await session.Plugins.DisableAsync(pluginPath);
            Tab(window, "Plugin");
            Control<ListBox>(window, "EnabledPluginList").SelectedItem = Control<ListBox>(window, "EnabledPluginList").Items.Cast<LightRcon.Core.Plugins.PluginDetails>().Single(item => item.Path == pluginPath);
            await Wait(() => Control<TabControl>(window, "PluginPages").IsEnabled && Control<TextBlock>(window, "PluginAuthorText").Text == "UI author");
            pluginPages.SelectedIndex = 1;
            var expandedInput = Control<StackPanel>(window, "PluginSettingFields").Children.OfType<TextBox>().Single(box => box.Tag as string == "Messages|Text");
            var expandButton = Control<StackPanel>(window, "PluginSettingFields").Children.OfType<Button>().First(button => button.Content as string == "...");
            _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<AppDialog>().Single();
                Visuals<TextBox>(dialog).Single().Text = "expanded\nmultiline";
                Capture(dialog, "plugin-expanded-editor");
                Visuals<Button>(dialog).Single(button => button.Content as string == UiText.Get("Apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            expandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => expandedInput.Text == "expanded\nmultiline");
            await Wait(() => Control<TextBlock>(window, "PluginPageStatus").Text == UiText.Get("Plugin settings saved."));
            Check((await session.Plugins.GetSettingsAsync(pluginPath)).Single(item => item.Name == "Messages|Text").Value == "expanded\nmultiline", "expanded WPF editor applies and saves multiline plugin settings");
            expandedInput.Text = "after";
            expandedInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, expandedInput, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            await Wait(() => Control<TextBlock>(window, "PluginPageStatus").Text == UiText.Get("Plugin settings saved."));
            Check(window.FindName("SavePluginSettingsButton") is null && Control<Button>(window, "ReloadPluginSettingsButton").TranslatePoint(new Point(), pluginPages).Y < 0 && Control<StackPanel>(window, "PluginSettingFields").Parent is ScrollViewer { Padding.Right: 50 }, "plugin settings autosave without a Save button, reserve scrollbar space and show Reload in the header");
            await session.Plugins.EnableAsync(pluginPath); await session.Plugins.RefreshAsync();
            await Wait(() => Control<Button>(window, "DisableSelectedPluginButton").IsVisible);
            Click(window, "ReloadPluginSettingsButton");
            await Wait(() => Control<TabControl>(window, "PluginPages").IsEnabled && Control<Button>(window, "DisableSelectedPluginButton").IsVisible);
            Check(session.Plugins.Snapshot().Single(item => item.Path == pluginPath).Enabled && !Control<Button>(window, "EnableSelectedPluginButton").IsVisible, "enabled plugin retains Disable after metadata reload and list refresh");
            await session.Plugins.DisableAsync(pluginPath);
            for (var index = 0; index < 520; index++) session.Journal.Add("Chat", "Limit", "all", "limit-row-" + index);
            Tab(window, "Chat");
            var boundedChat = Control<TextBox>(window, "ChatList");
            await Wait(() => boundedChat.Text.Contains("limit-row-519"));
            Check(boundedChat.Text.Split(Environment.NewLine).Length == 450 && !boundedChat.Text.Contains("limit-row-0\r"), "chat editor defaults to at most 450 logical lines");
            boundedChat.Select(0, 20);
            Check(boundedChat.SelectedText.Length == 20, "chat text supports selection for copying");
            Click(window, "ClearChatButton"); await Task.Delay(700);
            Check(boundedChat.Text.Length == 0 && session.Journal.Snapshot().Any(entry => entry.Text == "limit-row-519"), "Clear chat clears only the view and does not refill old messages or delete history");
            var lineInput = Control<TextBox>(window, "ChatLineLimitInput");
            lineInput.Text = "801"; lineInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Check(lineInput.Text == "450", "chat line preference rejects values outside 100–800");
            lineInput.Text = "100"; lineInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            for (var index = 0; index < 120; index++) session.Journal.Add("Chat", "Limit", "all", "small-limit-" + index);
            await Wait(() => boundedChat.Text.Contains("small-limit-119"));
            Check(boundedChat.Text.Split(Environment.NewLine).Length == 100 && new ProfileStore(data).State.ChatLineLimit == 100, "chat line input persists and applies the requested display limit");
            lineInput.Text = "450"; lineInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Tab(window, "Console");
            Check(Control<TextBox>(window, "ConsoleList").IsReadOnly && Control<TextBox>(window, "ConsoleList").AcceptsReturn, "console uses a selectable read-only text editor");
            var importChoices = Control<ComboBox>(window, "PluginImportPreferenceBox"); importChoices.SelectedValue = "keep";
            Check(new ProfileStore(data).State.PluginImportPreference == "keep", "plugin import preference saves without a separate action");
            Tab(window, "Plugin");
            var importFolder = Path.Combine(root, "drop-package"); Directory.CreateDirectory(importFolder);
            var dropSource = Path.Combine(importFolder, "DroppedPlugin.cs");
            File.WriteAllText(dropSource, "using LightRcon.Core.Plugins; [assembly: LightRconPlus_only(1)] public sealed class DroppedPlugin : ILightRconPlugin { public void Enable(PluginContext c) {} public void OnEvent(string[] w) {} public void Dispose() {} }");
            var dropData = new DataObject(DataFormats.FileDrop, new[] { dropSource });
            var drop = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [dropData, DragDropKeyStates.None, DragDropEffects.Copy, Control<ListBox>(window, "EnabledPluginList"), new Point()], null)!;
            drop.RoutedEvent = DragDrop.DropEvent;
            Control<ListBox>(window, "EnabledPluginList").RaiseEvent(drop);
            await Wait(() => session.Plugins.Snapshot().Any(item => item.Path == dropSource));
            Check(session.Profile.InstalledPlugins.Contains(dropSource), "dropping C# source imports from its original path when Keep is preferred");
            importChoices.SelectedValue = "ask";
            _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<AppDialog>().Single();
                Check(Visuals<Button>(dialog).Count() == 3 && Equals(dialog.Background, window.FindResource("Background")), "import confirmation offers Keep, Copy and Cancel with the current WPF theme");
                Visuals<Button>(dialog).Single(button => button.Content as string == UiText.Get("Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            await window.ImportPluginFilesAsync([dropSource]);
            Capture(window, "plugin-autosave");
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
            await Wait(() => Control<Button>(window, "DisableLayerButton").Visibility == Visibility.Visible);
            Check(Control<Button>(window, "EnableLayerButton").Visibility == Visibility.Collapsed && Equals(Control<Button>(window, "DisableLayerButton").Background, window.FindResource("Accent")) && !Control<TextBox>(window, "LayerPort").IsEnabled, "running listener offers only the blue disable action and locks its endpoint");
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
            Tab(layerWindow, "Plugin");
            await Wait(() => Control<TextBlock>(layerWindow, "PluginAuthorText").Text == "UI author" && Control<TabControl>(layerWindow, "PluginPages").IsEnabled);
            Check(Control<ListBox>(layerWindow, "EnabledPluginList").Items.Count == session.Plugins.Snapshot().Length && Control<Button>(layerWindow, "ImportPluginButton").Visibility == Visibility.Collapsed && !Control<Button>(layerWindow, "EnableSelectedPluginButton").IsEnabled, "Layer plugin page shows the host list without client import or unauthorized activation");
            var layerPluginPages = Control<TabControl>(layerWindow, "PluginPages"); layerPluginPages.SelectedIndex = 1;
            Check(Control<StackPanel>(layerWindow, "PluginSettingFields").Children.OfType<TextBox>().All(editor => !editor.IsEnabled), "Layer viewer can inspect plugin settings but cannot edit them");
            layerSelector.SelectedIndex = 1;
            Check(Control<TabItem>(layerWindow, "LayersTab").Visibility == Visibility.Visible && Control<Border>(layerWindow, "LayerListenerPanel").Visibility == Visibility.Collapsed, "Layer administrator sees upstream accounts without local listener");
            Tab(layerWindow, "Layers");
            await Wait(() => Control<DataGrid>(layerWindow, "LayerAccountsGrid").Items.Count == 2);
            Check(true, "Layer account management automatically loads upstream accounts");
            var layerAdminSession = ((ServerCard)layerCards.Items[1]).Session;
            Tab(layerWindow, "Plugin");
            await Wait(() => Control<TextBlock>(layerWindow, "PluginAuthorText").Text == "UI author" && Control<TabControl>(layerWindow, "PluginPages").IsEnabled);
            layerPluginPages.SelectedIndex = 1;
            var remotePluginInput = Control<StackPanel>(layerWindow, "PluginSettingFields").Children.OfType<TextBox>().Single(editor => editor.Tag as string == "Messages|Text");
            Check(remotePluginInput.IsEnabled, "Layer administrator can edit main Layer plugin settings");
            remotePluginInput.Text = "saved from Layer client"; remotePluginInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, remotePluginInput, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            await Wait(() => Control<TextBlock>(layerWindow, "PluginPageStatus").Text == UiText.Get("Plugin settings saved."));
            Check((await session.Plugins.GetSettingsAsync(pluginPath)).Single(item => item.Name == "Messages|Text").Value == "saved from Layer client", "Layer plugin Settings save updates the actual upstream runtime");
            Capture(layerWindow, "layer-plugin-settings");
            Click(layerWindow, "EnableSelectedPluginButton");
            await Wait(() => session.Plugins.Snapshot().Single(item => item.Path == pluginPath).Enabled && Control<Button>(layerWindow, "DisableSelectedPluginButton").IsVisible);
            Click(layerWindow, "DisableSelectedPluginButton");
            await Wait(() => !session.Plugins.Snapshot().Single(item => item.Path == pluginPath).Enabled);
            Check(true, "Layer activation buttons control the upstream plugin runtime");
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
                Check(gameEditors.Keys.ToHashSet().SetEquals(SettingsCatalog.For(game).Select(item => item.Command)) && !gameEditors.ContainsKey("vars.commander") && gameEditors.ContainsKey("vars.hacker") == (game == GameId.BFHL), game + " settings show only controls belonging to the detected game");
                Check(browser.Items.Cast<MapGroup>().SelectMany(group => group.Maps).Select(map => map.FileName).ToHashSet().SetEquals(MapCatalog.For(game).Select(map => map.FileName)), game + " map browser contains exactly its own game catalog");
                if (game == GameId.BF3) Check(((SettingChoiceBox)gameEditors["vars.gunMasterWeaponsPreset"]).Items.Count == 9, "BF3 desktop exposes its nine Gun Master weapon presets");
                var menu = gameWindow.BuildPlayerMenu(((ServerCard)gameCards.Items[0]).Session, new PlayerInfo { Name = "test", Team = 1 });
                Check(!((MenuItem)((MenuItem)menu.Items[3]).Items[1]).IsEnabled && ((MenuItem)((MenuItem)menu.Items[3]).Items[0]).IsEnabled, game + " offers Battlelog while disabling BF4-only profile search");
                await Close(gameWindow);
            }
        }
        catch (Exception error) { Console.Error.WriteLine("Desktop test failure: " + error); throw; }
        finally
        {
            foreach (var remaining in Application.Current.Windows.OfType<MainWindow>().ToArray())
                try { await Close(remaining); } catch (Exception error) { Console.Error.WriteLine("Window cleanup: " + error.Message); }
            UiText.SetLanguage("en");
            try { Directory.Delete(root, true); } catch (IOException error) { Console.Error.WriteLine("Temporary files retained: " + error.Message); }
        }
    }
}
