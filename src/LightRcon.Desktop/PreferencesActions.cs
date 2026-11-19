using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private void ChatLineLimit_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => SaveChatLineLimit();
    private void ChatLineLimit_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SaveChatLineLimit(); e.Handled = true; } }
    private void SaveChatLineLimit()
    {
        if (!ready || closing) return;
        if (!int.TryParse(ChatLineLimitInput.Text, out var value) || value is < 100 or > 800)
        {
            AppPreferenceStatus.Text = UiText.Get("Enter a whole number from 100 to 800.");
            ChatLineLimitInput.Text = store.State.ChatLineLimit.ToString(); return;
        }
        store.State.ChatLineLimit = value; store.Save();
        AppPreferenceStatus.Text = UiText.Get("Preferences saved."); UpdateChat();
    }
    private void PluginImportPreference_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || closing || PluginImportPreferenceBox.SelectedValue is not string value) return;
        store.State.PluginImportPreference = value; store.Save();
        AppPreferenceStatus.Text = UiText.Get("Preferences saved.");
    }
    private void ClearAllLogs_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm(UiText.Get("Delete all local chat and event log files? This cannot be undone. New logs will continue to be recorded."))) return;
        try { store.ClearAllLogs(); AppPreferenceStatus.Text = UiText.Get("All local log files cleared."); }
        catch (Exception error) { AppPreferenceStatus.Text = error.Message; }
    }
}
