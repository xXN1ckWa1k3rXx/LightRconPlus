using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private void DisplayBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (YellTimePanel is not null) YellTimePanel.Visibility = ComboText(DisplayBox) == "Yell" ? Visibility.Visible : Visibility.Collapsed; }
    private async void SendChat_Click(object sender, RoutedEventArgs e) => await SendChat();
    private async void ChatInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SendChat(); } }
    private async Task SendChat()
    {
        await Run(async () =>
        {
            var s = RequireSession(); var text = ChatInput.Text; var yell = ComboText(DisplayBox) == "Yell"; if (string.IsNullOrWhiteSpace(text)) return;
            if (text.Length >= (yell ? 256 : 128)) throw new ArgumentException(UiText.Format("{0} messages must be shorter than {1} characters.", yell ? "Yell" : "Say", yell ? 256 : 128));
            var target = AudienceCode; List<string> words = [yell ? "admin.yell" : "admin.say", text];
            if (yell) words.Add(((int?)YellTimeBox.SelectedItem ?? 10).ToString()); words.Add(target == "All Players" ? "all" : "player"); if (target != "All Players") words.Add(target);
            await s.ExecuteAsync(words.ToArray());
            if (selected == s && ChatInput.Text == text) ChatInput.Clear(); SaveClient(); store.Save();
        });
    }
    private async void SendConsole_Click(object sender, RoutedEventArgs e) => await SendConsole();
    private async void ConsoleInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SendConsole(); } }
    private async Task SendConsole()
    {
        await Run(async () =>
        {
            var s = RequireSession(); if (!s.Permissions.HasFlag(LayerPermission.Console)) throw new UnauthorizedAccessException(UiText.Get("You do not have console permission."));
            var words = CommandText.Parse(ConsoleInput.Text);
            bool readOnly;
            try { readOnly = LayerAuthorization.Required(words) == LayerPermission.View; }
            catch (UnauthorizedAccessException) { readOnly = false; }
            if (!readOnly && !Confirm(UiText.Get("Execute this RCON command?\n") + ConsoleInput.Text)) return; await s.ExecuteConsoleAsync(words); ConsoleInput.Clear();
        });
    }
}
