using System.Windows;
using System.Windows.Controls;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private async void EnableLayer_Click(object sender, RoutedEventArgs e)
    {
        await Run(async () =>
        {
            var s = RequireSession(); if (s.IsHosting) throw new ArgumentException(UiText.Get("Disable Layer before changing its listening address."));
            if (!int.TryParse(LayerPort.Text, out var port)) throw new ArgumentException(UiText.Get("Enter a valid port."));
            lock (s.Profile.Sync) { s.Profile.Layer.BindAddress = LayerBind.Text.Trim(); s.Profile.Layer.Port = port; } await s.StartLayerAsync();
        });
    }
    private async void DisableLayer_Click(object sender, RoutedEventArgs e) { if (Confirm(UiText.Get("Disable Layer and disconnect all downstream clients?"))) await Run(() => RequireSession().StopLayerAsync()); }
    private async Task LoadLayerAccountsAsync(bool showError)
    {
        var s = selected; if (s is null) return;
        if (s.Profile.IsLayer && (!s.IsConnected || !s.Permissions.HasFlag(LayerPermission.LayerAdmin))) { displayedLayer = null; LayerAccountsGrid.ItemsSource = null; return; }
        try
        {
            var options = await s.GetLayerOptionsAsync(s.Profile.IsLayer); if (selected != s) return;
            displayedLayer = options; LayerAccountsGrid.ItemsSource = options.Accounts; GlobalPermissionsText.Text = UiText.Get("Global default permissions: ") + options.GlobalPermissions;
        }
        catch (Exception ex) { displayedLayer = null; LayerAccountsGrid.ItemsSource = null; GlobalPermissionsText.Text = ex.Message; if (showError) ShowError(ex); }
    }
    private async void RefreshLayerAccounts_Click(object sender, RoutedEventArgs e) => await LoadLayerAccountsAsync(true);
    private async void GlobalPermissions_Click(object sender, RoutedEventArgs e)
    {
        if (displayedLayer is null) { await LoadLayerAccountsAsync(true); if (displayedLayer is null) return; }
        var dialog = new FormDialog(this, UiText.Get("Global permissions"), UiText.Get("Used when an account has no individual permissions. View is required to sign in."));
        var editor = new PermissionEditor(displayedLayer.GlobalPermissions); dialog.Fields.Children.Add(editor);
        if (dialog.ShowDialog() == true) await Run(async () => { await RequireSession().SetGlobalPermissionsAsync(RequireSession().Profile.IsLayer, editor.Value); await LoadLayerAccountsAsync(false); });
    }
    private async void AddLayerAccount_Click(object sender, RoutedEventArgs e) => await EditLayerAccount(null);
    private async void EditLayerAccount_Click(object sender, RoutedEventArgs e) { if (LayerAccountsGrid.SelectedItem is PublicLayerAccount account) await EditLayerAccount(account); }
    private async Task EditLayerAccount(PublicLayerAccount? account)
    {
        var dialog = new FormDialog(this, account is null ? UiText.Get("Add Layer account") : UiText.Get("Edit Layer account"), UiText.Get("Use a separate password. Individual permissions override global defaults."));
        var username = dialog.Field("Username", new TextBox { Text = account?.Username ?? "", IsReadOnly = account is not null });
        var password = dialog.Field(account is null ? UiText.Get("Password · At least 12 characters") : UiText.Get("Password · Leave blank to keep current password"), new PasswordBox());
        var enabled = new CheckBox { Content = UiText.Get("Enable account"), IsChecked = account?.Enabled ?? true }; dialog.Fields.Children.Add(enabled);
        var inherit = new CheckBox { Content = UiText.Get("Inherit global permissions"), IsChecked = account?.Permissions is null }; dialog.Fields.Children.Add(inherit);
        var permissions = new PermissionEditor(account?.Permissions ?? LayerPermission.View); dialog.Fields.Children.Add(permissions);
        inherit.Checked += (_, _) => permissions.IsEnabled = false; inherit.Unchecked += (_, _) => permissions.IsEnabled = true; permissions.IsEnabled = inherit.IsChecked != true;
        dialog.Save = () => { if (string.IsNullOrWhiteSpace(username.Text)) throw new ArgumentException(UiText.Get("Enter a username.")); if ((account is null || password.Password.Length > 0) && password.Password.Length < 12) throw new ArgumentException(UiText.Get("Password must contain at least 12 characters.")); return true; };
        if (dialog.ShowDialog() != true) return;
        await Run(async () => { await RequireSession().SaveLayerAccountAsync(RequireSession().Profile.IsLayer, username.Text.Trim(), password.Password, enabled.IsChecked == true, inherit.IsChecked == true ? null : permissions.Value); await LoadLayerAccountsAsync(false); });
    }
    private async void DeleteLayerAccount_Click(object sender, RoutedEventArgs e)
    {
        if (LayerAccountsGrid.SelectedItem is not PublicLayerAccount account || !Confirm(UiText.Format("Delete Layer account {0}?", account.Username))) return;
        await Run(async () => { await RequireSession().DeleteLayerAccountAsync(RequireSession().Profile.IsLayer, account.Username); await LoadLayerAccountsAsync(false); });
    }
}
