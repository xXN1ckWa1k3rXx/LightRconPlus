using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using LightRcon.Core;
using LightRcon.Core.Plugins;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private ServerSession? pluginSession;
    private bool updatingPlugins;
    private int pluginRequest;
    private bool pluginBusy;
    private readonly SemaphoreSlim pluginOperations = new(1);
    private Task pendingPluginSave = Task.CompletedTask;
    private string pluginLanguage = "";
    private sealed class PluginEditor(PluginSetting setting, Func<string> read)
    {
        public PluginSetting Setting { get; set; } = setting;
        public Func<string> Read { get; } = read;
    }
    private readonly Dictionary<string, PluginEditor> pluginEditors = [];
    private void RefreshPluginList(ServerSession session)
    {
        if (EnabledPluginList is null) return;
        var changedSession = pluginSession != session;
        pluginSession = session;
        PluginPathPanel.Visibility = session.Profile.IsLayer ? Visibility.Collapsed : Visibility.Visible;
        if (session.Profile.IsLayer) PluginPathText.Text = "";
        var plugins = session.Plugins.Snapshot();
        if (changedSession || pluginLanguage != UiText.Language || EnabledPluginList.ItemsSource is not PluginDetails[] previous || !previous.SequenceEqual(plugins))
        {
            pluginLanguage = UiText.Language;
            var previousPlugin = changedSession ? null : EnabledPluginList.SelectedItem as PluginDetails;
            var path = previousPlugin?.Path;
            updatingPlugins = true;
            EnabledPluginList.ItemsSource = plugins;
            EnabledPluginList.SelectedItem = plugins.FirstOrDefault(item => item.Path == path) ?? plugins.FirstOrDefault();
            updatingPlugins = false;
            if (changedSession || path is null || (EnabledPluginList.SelectedItem as PluginDetails)?.Path != path) _ = LoadSelectedPluginAsync();
            else if (!pluginBusy && EnabledPluginList.SelectedItem is PluginDetails currentPlugin
                && (previousPlugin?.Enabled != currentPlugin.Enabled || previousPlugin?.Error != currentPlugin.Error || previousPlugin?.Kind != currentPlugin.Kind))
                PluginPageStatus.Text = currentPlugin.Kind + " · " + currentPlugin.Status;
        }
        var selectedPlugin = plugins.FirstOrDefault(item => item.Path == (EnabledPluginList.SelectedItem as PluginDetails)?.Path);
        ProconBanner.Visibility = selectedPlugin is { Kind: "Procon" } ? Visibility.Visible : Visibility.Collapsed;
        if (selectedPlugin is { Error.Length: > 0 }) PluginErrorText.Text = selectedPlugin.Error;
        ImportPluginButton.Visibility = session.Profile.IsLayer ? Visibility.Collapsed : Visibility.Visible;
        ImportPluginButton.IsEnabled = !pluginBusy && session.IsConnected && !session.Profile.IsLayer;
        var canToggle = !pluginBusy && !session.Plugins.IsRestoring && session.IsConnected && (!session.Profile.IsLayer || session.Permissions.HasFlag(LayerPermission.LayerAdmin));
        EnableSelectedPluginButton.Visibility = selectedPlugin is { Enabled: true } ? Visibility.Collapsed : Visibility.Visible;
        DisableSelectedPluginButton.Visibility = selectedPlugin is { Enabled: true } ? Visibility.Visible : Visibility.Collapsed;
        EnableSelectedPluginButton.IsEnabled = canToggle && selectedPlugin is { Enabled: false };
        DisableSelectedPluginButton.IsEnabled = canToggle && selectedPlugin is { Enabled: true };
        ReloadPluginSettingsButton.IsEnabled = !pluginBusy && session.IsConnected && selectedPlugin is not null;
        RefreshPluginListButton.IsEnabled = !pluginBusy && session.IsConnected;
    }
    private bool CanEditPluginSettings => selected is { } session && (!session.Profile.IsLayer || session.Permissions.HasFlag(LayerPermission.Settings));
    private async void ImportPlugin_Click(object sender, RoutedEventArgs args)
    {
        var session = RequireSession();
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = UiText.Get("Plugin files") + " (*.cs, *.dll, *.lua)|*.cs;*.dll;*.lua", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await ImportPluginFilesAsync([dialog.FileName]);
    }
    internal async Task ImportPluginFilesAsync(IEnumerable<string> files)
    {
        if (selected is not { IsConnected: true } session || session.Profile.IsLayer) return;
        foreach (var file in files)
        {
            if (Path.GetExtension(file).ToLowerInvariant() is not (".cs" or ".dll" or ".lua")) continue;
            var choice = store.State.PluginImportPreference;
            if (choice == "ask") choice = AppDialog.ImportChoice(this, file);
            if (choice is null) continue;
            await PluginOperation(async () =>
            {
                var path = choice == "copy" ? await PluginLibrary.CopyAsync(file, session.Game) : Path.GetFullPath(file);
                await session.Plugins.ImportAsync(path);
                if (selected != session) return;
                RefreshPluginList(session);
                EnabledPluginList.SelectedItem = ((PluginDetails[])EnabledPluginList.ItemsSource).FirstOrDefault(plugin => plugin.Path == path);
                await LoadSelectedPluginAsync();
            });
        }
    }
    private void PluginList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = selected is { IsConnected: true, Profile.IsLayer: false } && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(path => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".dll" or ".lua") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void PluginList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await ImportPluginFilesAsync(files);
    }
    private async void RefreshPluginList_Click(object sender, RoutedEventArgs e) => await PluginOperation(async () =>
    {
        if (selected is not { } session) return;
        await session.Plugins.RefreshAsync(); RefreshPluginList(session);
    });
    private async void EnableSelectedPlugin_Click(object sender, RoutedEventArgs args)
    {
        if (selected is not { } session || EnabledPluginList.SelectedItem is not PluginDetails plugin) return;
        await PluginOperation(async () => { await session.Plugins.EnableAsync(plugin.Path); await LoadSelectedPluginAsync(); });
    }
    private async void DisablePlugin_Click(object sender, RoutedEventArgs args)
    {
        if (selected is not { } session || EnabledPluginList.SelectedItem is not PluginDetails plugin) return;
        await PluginOperation(async () => { await session.Plugins.DisableAsync(plugin.Path); await LoadSelectedPluginAsync(); });
    }
    private async void PluginSelection_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!updatingPlugins) await LoadSelectedPluginAsync();
    }
    private Task LoadSelectedPluginAsync() => LoadSelectedPluginAsync(false);
    private async Task LoadSelectedPluginAsync(bool retryFailed)
    {
        var request = ++pluginRequest;
        pluginEditors.Clear();
        PluginSettingFields.Children.Clear();
        PluginPages.IsEnabled = false;
        if (selected is not { } session || EnabledPluginList.SelectedItem is not PluginDetails plugin)
        {
            PluginNameText.Text = PluginErrorText.Text = "";
            ProconBanner.Visibility = Visibility.Collapsed;
            PluginPageStatus.Text = UiText.Get("Select a plugin to view its details and settings."); return;
        }
        PluginNameText.Text = plugin.Name; PluginPathText.Text = session.Profile.IsLayer ? "" : plugin.Path;
        PluginVersionText.Text = plugin.Version; PluginAuthorText.Text = plugin.Author; PluginWebsiteText.Text = plugin.Website;
        PluginDescriptionText.Text = "";
        PluginErrorText.Text = plugin.Error;
        PluginPageStatus.Text = plugin.Kind + " · " + plugin.Status;
        RefreshPluginList(session);
        try
        {
            if (retryFailed && !session.Profile.IsLayer && session.Plugins.Snapshot().Any(item => item.Path == plugin.Path && item.Error.Length > 0))
                await session.Plugins.ImportAsync(plugin.Path);
            var (details, settings) = await session.Plugins.GetViewAsync(plugin.Path, refresh: retryFailed);
            if (request != pluginRequest || selected != session) return;
            PluginNameText.Text = details.Name;
            PluginVersionText.Text = details.Version;
            PluginAuthorText.Text = details.Author;
            PluginWebsiteText.Text = details.Website;
            PluginDescriptionText.Text = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(details.Description, @"<(br\s*/?|/p|/h\d|/li)>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", ""));
            PluginPathText.Text = session.Profile.IsLayer ? "" : details.Path;
            PluginErrorText.Text = details.Error;
            PluginPageStatus.Text = details.Kind + " · " + details.Status;
            BuildPluginSettings(settings);
            PluginPages.IsEnabled = true;
        }
        catch (Exception error)
        {
            if (request != pluginRequest || selected != session) return;
            PluginPageStatus.Text = UiText.Get("Plugin failed");
            PluginErrorText.Text = UiText.Get(error.GetBaseException().Message);
            PluginPages.IsEnabled = true;

        }
        if (selected == session) RefreshPluginList(session);
    }
    private void BuildPluginSettings(PluginSetting[] settings)
    {
        pluginEditors.Clear(); PluginSettingFields.Children.Clear();
        if (settings.Length == 0) PluginSettingFields.Children.Add(new TextBlock { Text = UiText.Get("This plugin has no settings."), TextWrapping = TextWrapping.Wrap });
        if (selected is not { } editingSession || EnabledPluginList.SelectedItem is not PluginDetails editingPlugin) return;
        string? group = null;
        foreach (var setting in settings)
        {
            if (setting.Group != group && setting.Group.Length > 0)
                PluginSettingFields.Children.Add(new TextBlock { Text = setting.Group, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 16, 0, 8) });
            group = setting.Group;
            PluginSettingFields.Children.Add(new TextBlock { Text = setting.Label + (setting.ReadOnly ? " · " + UiText.Get("Read-only") : ""), Tag = setting, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 6), Foreground = (Brush)FindResource("Muted") });
            FrameworkElement editor;
            Button? expand = null;
            Func<string> read;
            if (setting.Type.Equals("bool", StringComparison.OrdinalIgnoreCase) || setting.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            {
                var box = new CheckBox { IsChecked = bool.TryParse(setting.Value, out var enabled) && enabled, Content = UiText.Get("Enabled") };
                LocExtension.Bind(box, ContentControl.ContentProperty, "Enabled");
                editor = box; read = () => box.IsChecked == true ? "True" : "False";
            }
            else if (setting.Choices.Length > 0 || setting.Type is "onoff" or "yesno")
            {
                var choices = setting.Type switch { "onoff" => new[] { "On", "Off" }, "yesno" => new[] { "Yes", "No" }, _ => setting.Choices };
                var box = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(choice => choice.Equals(setting.Value, StringComparison.OrdinalIgnoreCase)), MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
                editor = box; read = () => box.SelectedItem as string ?? setting.Value;
            }
            else
            {
                var array = setting.Type.Equals("stringarray", StringComparison.OrdinalIgnoreCase);
                var multiline = array || setting.Type is "multiline" or "string";
                var value = array ? string.Join(Environment.NewLine, setting.Value.Split('|').Select(item => WebUtility.UrlDecode(item))) : setting.Value;
                var box = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 80 : 32, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                if (multiline)
                {
                    expand = new Button { Content = "...", Width = 36, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 4, 0, 0), ToolTip = UiText.Get("Open larger editor") };
                    expand.Click += async (_, _) =>
                    {
                        if (AppDialog.EditText(this, setting.Label, box.Text, setting.ReadOnly || !CanEditPluginSettings) is { } text)
                        { box.Text = text; await SavePluginEditorAsync(editingSession, editingPlugin.Path, pluginEditors[setting.Name]); }
                    };
                }
                editor = box; read = () => array ? string.Join('|', box.Text.Replace("\r", "").Split('\n').Select(item => WebUtility.UrlEncode(item))) : box.Text;
            }
            editor.IsEnabled = !setting.ReadOnly && CanEditPluginSettings;
            editor.Tag = setting.Name;
            PluginSettingFields.Children.Add(editor);
            if (expand is not null) PluginSettingFields.Children.Add(expand);
            if (!setting.ReadOnly)
            {
                var state = new PluginEditor(setting, read);
                pluginEditors[setting.Name] = state;
                editor.LostKeyboardFocus += async (_, _) =>
                {
                    pendingPluginSave = SavePluginEditorAsync(editingSession, editingPlugin.Path, state);
                    await pendingPluginSave;
                };
            }
        }
    }
    private async Task SavePluginEditorAsync(ServerSession session, string path, PluginEditor item)
    {
        if (!session.IsConnected || session.Profile.IsLayer && !session.Permissions.HasFlag(LayerPermission.Settings)) return;
        var value = item.Read();
        if (item.Setting.MatchesValue(value)) return;
        await PluginOperation(async () =>
        {
            if (item.Setting.MatchesValue(value)) return;
            await session.Plugins.SetSettingAsync(path, item.Setting.Name, value);
            item.Setting = item.Setting with { Value = value };
            if (selected == session && (EnabledPluginList.SelectedItem as PluginDetails)?.Path == path)
                PluginPageStatus.Text = UiText.Get("Plugin settings saved.");
        });
    }
    private async void ReloadPluginSettings_Click(object sender, RoutedEventArgs args) => await PluginOperation(() => LoadSelectedPluginAsync(true));
    private void RefreshPluginLanguage()
    {
        foreach (var label in PluginSettingFields.Children.OfType<TextBlock>())
            if (label.Tag is PluginSetting setting) label.Text = setting.Label + (setting.ReadOnly ? " · " + UiText.Get("Read-only") : "");
        if (EnabledPluginList.SelectedItem is PluginDetails plugin) PluginPageStatus.Text = plugin.Kind + " · " + plugin.Status;
        else PluginPageStatus.Text = UiText.Get("Select a plugin to view its details and settings.");
    }
    private async Task PluginOperation(Func<Task> operation)
    {
        await pluginOperations.WaitAsync();
        pluginBusy = true;
        if (selected is { } session) RefreshPluginList(session);
        try { await operation(); }
        catch (Exception error) { if (!closing) PluginPageStatus.Text = UiText.Get(error.GetBaseException().Message); }
        finally { pluginBusy = false; if (selected is { } current) RefreshPluginList(current); pluginOperations.Release(); }
    }
}
