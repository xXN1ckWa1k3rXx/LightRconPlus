using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private void PlayersGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not PlayerInfo player || selected is not { } session) { PlayersGrid.ContextMenu = null; return; }
        PlayersGrid.SelectedItem = player;
        PlayersGrid.ContextMenu = BuildPlayerMenu(session, player);
    }
    internal ContextMenu BuildPlayerMenu(ServerSession session, PlayerInfo player)
    {
        var menu = new ContextMenu();
        var canAct = session.IsConnected && session.Permissions.HasFlag(LayerPermission.Players);
        var move = new MenuItem { Header = UiText.Get("Move to team"), IsEnabled = canAct };
        for (var team = 1; team <= Math.Clamp(session.Info.TeamScores.Length, 2, 16); team++)
        {
            var destination = team;
            var item = new MenuItem { Header = TeamCatalog.Name(session.Info.Map, session.Info.GameMode, team, session.Game), IsEnabled = team != player.Team };
            item.Click += async (_, _) => await Run(() => session.ExecuteAsync(["admin.movePlayer", player.Name, destination.ToString(System.Globalization.CultureInfo.InvariantCulture), "0", "true"]));
            move.Items.Add(item);
        }
        var kill = new MenuItem { Header = UiText.Get("Kill"), IsEnabled = canAct };
        kill.Click += async (_, _) => await Run(() => session.ExecuteAsync(["admin.killPlayer", player.Name]));
        var profile = new MenuItem { Header = UiText.Get("Player profile") };
        var battlelog = new MenuItem { Header = "Battlelog" }; battlelog.Click += (_, _) => OpenPlayerProfile(session.Game, player.Name, false);
        var bf4db = new MenuItem { Header = "BF4DB", IsEnabled = session.Game == GameId.BF4 }; bf4db.Click += (_, _) => OpenPlayerProfile(session.Game, player.Name, true);
        profile.Items.Add(battlelog); profile.Items.Add(bf4db);
        menu.Items.Add(move); menu.Items.Add(kill); menu.Items.Add(new Separator()); menu.Items.Add(profile);
        return menu;
    }
    private void OpenPlayerProfile(GameId game, string name, bool bf4db)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(GameCatalog.PlayerProfile(game, name, bf4db).AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void PlayersGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (Ancestor<DataGridRow>(source) is not null || Ancestor<DataGridColumnHeader>(source) is not null || Ancestor<ScrollBar>(source) is not null || Ancestor<Thumb>(source) is not null) return;
        PlayersGrid.UnselectAll();
        PlayersGrid.CurrentCell = new DataGridCellInfo();
    }
    private void PlayersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateSelectedPlayer();
    private void UpdateSelectedPlayer()
    {
        if (PlayersGrid.SelectedItem is not PlayerInfo p) { PlayerActionPanel.Visibility = Visibility.Collapsed; return; }
        PlayerActionPanel.Visibility = Visibility.Visible; SelectedPlayerName.Text = p.Name;
        string Known(string x) => string.IsNullOrEmpty(x) ? UiText.Get("Not available") : x;
        PlayerIdentity.Text = $"EA GUID  {Known(p.EaGuid)}\nPB GUID  {Known(p.PbGuid)}    IP  {Known(p.Ip)}    {UiText.Format("Team {0}", p.Team)}";
    }
    private PlayerInfo RequirePlayer() => PlayersGrid.SelectedItem as PlayerInfo ?? throw new ArgumentException(UiText.Get("Select a player first."));
    private static string SafePb(string text)
    {
        if (text.IndexOfAny(['\r', '\n', ';', '"', '\\', '\0']) >= 0) throw new ArgumentException(UiText.Get("PunkBuster arguments cannot contain quotes, newlines or command separators.")); return text;
    }
    private static string PbSlot(PlayerInfo p) => p.PbSlot.Length > 0 ? p.PbSlot : throw new ArgumentException(UiText.Get("No PunkBuster slot received for this player. Refresh and wait for the pb_sv_plist response."));
    private async void KillPlayer_Click(object sender, RoutedEventArgs e)
    {
        await Run(async () => { var s = RequireSession(); var p = RequirePlayer(); if (ComboText(ActionProvider) == "PunkBuster") throw new ArgumentException(UiText.Get("PunkBuster has no kill command. Select Server.")); if (Confirm(UiText.Format("Kill {0}?", p.Name))) await s.ExecuteAsync(["admin.killPlayer", p.Name]); });
    }
    private async void KickPlayer_Click(object sender, RoutedEventArgs e)
    {
        await Run(async () =>
        {
            var s = RequireSession(); var p = RequirePlayer(); if (!Confirm(UiText.Format("Kick {0}?", p.Name))) return;
            if (ComboText(ActionProvider) == "PunkBuster") await s.ExecuteAsync(["punkBuster.pb_sv_command", $"pb_sv_kick {PbSlot(p)} 0 {CommandText.Quote(SafePb(PlayerReason.Text))}"]);
            else await s.ExecuteAsync(["admin.kickPlayer", p.Name, PlayerReason.Text]);
        });
    }
    private async void TempBanPlayer_Click(object sender, RoutedEventArgs e) => await BanPlayer(false);
    private async void PermanentBanPlayer_Click(object sender, RoutedEventArgs e) => await BanPlayer(true);
    internal static int BanDurationMinutes(string amount, string unit)
    {
        var factor = unit switch { "minutes" => 1, "hours" => 60, "days" => 1440, "weeks" => 10080, "months" => 43200, _ => 0 };
        if (factor == 0 || !int.TryParse(amount, out var count) || count < 1 || (long)count * factor > 525600)
            throw new ArgumentException(UiText.Get("Enter a positive whole duration, up to 365 days. Months are 30 days."));
        return count * factor;
    }
    private async Task BanPlayer(bool permanent)
    {
        await Run(async () =>
        {
            var s = RequireSession(); var p = RequirePlayer();
            var unit = ComboText(BanDurationUnit);
            var minutes = permanent ? 0 : BanDurationMinutes(BanDurationAmount.Text, unit);
            var provider = ComboText(ActionProvider); var reason = PlayerReason.Text;
            if (!Confirm(permanent ? UiText.Format("Permanently ban {0}?", p.Name) : UiText.Format("Ban {0} for {1} {2}?", p.Name, BanDurationAmount.Text.Trim(), UiText.Get(unit)))) return;
            await ExecutePlayerBanAsync(s, p, provider, permanent, minutes, reason);
            await s.RefreshAsync();
        });
    }
    internal static async Task ExecutePlayerBanAsync(ServerSession s, PlayerInfo p, string provider, bool permanent, int minutes, string reason)
    {
        if (!permanent && minutes is < 1 or > 525600) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (provider == "PunkBuster")
        {
            var time = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await s.ExecuteAsync(["punkBuster.pb_sv_command", permanent ? $"pb_sv_ban {PbSlot(p)} {CommandText.Quote(SafePb(reason))}" : $"pb_sv_kick {PbSlot(p)} {time} {CommandText.Quote(SafePb(reason))}"]);
            if (permanent) await s.ExecuteAsync(["punkBuster.pb_sv_command", "pb_sv_updbanfile"]);
        }
        else
        {
            if (p.EaGuid.Length == 0) throw new ArgumentException(UiText.Get("No EA GUID received; cannot ban by GUID."));
            await AddBan(s, "guid", p.EaGuid, permanent ? "perm" : "seconds", checked(minutes * 60).ToString(System.Globalization.CultureInfo.InvariantCulture), reason);
            await s.ExecuteAsync(["admin.kickPlayer", p.Name, reason]);
        }
    }
    private async void Restart_Click(object sender, RoutedEventArgs e) { if (Confirm(UiText.Get("Restart the current round now?"))) await Command(["mapList.restartRound"]); }
    private async void NextRound_Click(object sender, RoutedEventArgs e) { if (Confirm(UiText.Get("Switch to the next round now?"))) await Command(["mapList.runNextRound"]); }
    private void BanSearch_TextChanged(object sender, TextChangedEventArgs e) { if (ready) FilterBans(); }
    private void FilterBans()
    {
        if (selected is null) return; var query = BanSearch.Text.Trim(); var old = BansGrid.SelectedItem as BanEntry;
        var bans = selected.BanSnapshot().Where(x => query.Length == 0 || $"{x.Identifier} {x.Reason} {x.BanType}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        BansGrid.ItemsSource = bans; BansGrid.SelectedItem = bans.FirstOrDefault(x => old is not null && x.IdType == old.IdType && x.Identifier == old.Identifier);
    }
    private static async Task AddBan(ServerSession s, string idType, string identifier, string type, string amount, string reason)
    {
        if (string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException(UiText.Get("Ban identifier cannot be empty.")); if (reason.Length > 80) throw new ArgumentException(UiText.Get("Ban reasons may contain at most 80 characters."));
        var args = new List<string> { "banList.add", idType, identifier, type };
        if (type != "perm") { if (!int.TryParse(amount, out var n) || n < 1) throw new ArgumentException(UiText.Get("Duration must be a positive integer.")); args.Add(amount); }
        args.Add(reason); await s.ExecuteAsync(args.ToArray()); await s.ExecuteAsync(["banList.save"]);
    }
    internal static int ManualBanSeconds(string amount, string unit)
    {
        var factor = unit switch { "seconds" => 1, "minutes" => 60, "hours" => 3600, "days" => 86400, "weeks" => 604800, "months" => 2592000, _ => 0 };
        if (factor == 0 || !int.TryParse(amount, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 1)
            throw new ArgumentException(UiText.Get("Duration must be a positive integer."));
        if ((long)count * factor > int.MaxValue) throw new ArgumentException(UiText.Get("Duration is too large."));
        return count * factor;
    }
    private void ManualBanType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BanDurationValue is null || ManualBanDurationUnit is null) return;
        var type = ComboText(BanDurationType);
        BanDurationValue.IsEnabled = type != "perm";
        ManualBanDurationUnit.IsEnabled = type == "seconds";
    }
    private async void AddBan_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var s = RequireSession(); var type = ComboText(BanDurationType);
        var amount = type == "seconds" ? ManualBanSeconds(BanDurationValue.Text, ComboText(ManualBanDurationUnit)).ToString(System.Globalization.CultureInfo.InvariantCulture) : BanDurationValue.Text;
        await AddBan(s, ComboText(BanIdType), BanIdentifier.Text.Trim(), type, amount, ManualBanReason.Text); await s.RefreshAsync();
    });
    private async void ClearBans_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm(UiText.Get("Clear and save all server bans? This cannot be undone."))) return;
        await Run(async () => { var s = RequireSession(); await s.ExecuteAsync(["banList.clear"]); await s.ExecuteAsync(["banList.save"]); await s.RefreshAsync(); });
    }
    private void BansGrid_RightClick(object sender, MouseButtonEventArgs e) { var row = Ancestor<DataGridRow>(e.OriginalSource as DependencyObject); if (row is not null) row.IsSelected = true; }
    private async void RemoveBan_Click(object sender, RoutedEventArgs e)
    {
        if (BansGrid.SelectedItem is not BanEntry ban || !Confirm(UiText.Format("Remove the ban for {0}?", ban.Identifier))) return;
        await Run(async () => { var s = RequireSession(); await s.ExecuteAsync(["banList.remove", ban.IdType, ban.Identifier]); await s.ExecuteAsync(["banList.save"]); await s.RefreshAsync(); });
    }
    private async void EditBan_Click(object sender, RoutedEventArgs e)
    {
        if (BansGrid.SelectedItem is not BanEntry ban) return; var edit = FormDialog.Ban(this, ban); if (edit is not { } value) return;
        await Run(async () => { var s = RequireSession(); await AddBan(s, ban.IdType, ban.Identifier, value.Duration, value.Amount, value.Reason); await s.RefreshAsync(); });
    }
    private async Task Slot(string prefix, string action, string? name) => await Run(async () => { var s = RequireSession(); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(UiText.Get("Enter or select a player name.")); await s.ExecuteAsync([prefix + "." + action, name.Trim()]); await s.ExecuteAsync([prefix + ".save"]); await s.RefreshAsync(); });
    private async void AddSpectator_Click(object sender, RoutedEventArgs e) => await Slot("spectatorList", "add", SpectatorName.Text);
    private async void RemoveSpectator_Click(object sender, RoutedEventArgs e) => await Slot("spectatorList", "remove", SpectatorList.SelectedItem?.ToString());
    private async void AddReserved_Click(object sender, RoutedEventArgs e) => await Slot("reservedSlotsList", "add", ReservedName.Text);
    private async void RemoveReserved_Click(object sender, RoutedEventArgs e) => await Slot("reservedSlotsList", "remove", ReservedList.SelectedItem?.ToString());
}
