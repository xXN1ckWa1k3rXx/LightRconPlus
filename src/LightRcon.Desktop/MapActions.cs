using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private void MapSearch_TextChanged(object sender, TextChangedEventArgs e) { if (ready) UpdateMapGroups(); }
    private async void AddMap_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModeChoice mode) return;
        var s = RequireSession(); var entry = mode.Entry;
        await Run(() => s.MutateMapsAsync(maps => { maps.Add(entry); return maps; }));
    }
    private async void RefreshMaps_Click(object sender, RoutedEventArgs e) { var s = selected; if (s is not null) await Run(() => s.RefreshMapsAsync()); }
    private async void LoadMapList_Click(object sender, RoutedEventArgs e)
    {
        var s = selected;
        if (s is not null && Confirm(UiText.Get("Load the server MapList.txt and replace the current rotation?")))
            await Run(() => s.LoadMapsFromDiskAsync());
    }
    private async void ClearMaps_Click(object sender, RoutedEventArgs e) { if (Confirm(UiText.Get("Clear the entire current map list? The server’s map rotation will also be cleared."))) await Run(() => RequireSession().ReplaceMapsAsync([])); }
    private async void RemoveMap_Click(object sender, RoutedEventArgs e)
    {
        var s = selected; var index = CurrentMapsList.SelectedIndex; if (s is null || index < 0) return;
        var snapshot = s.Profile.DesiredMaps?.ToArray() ?? s.MapSnapshot();
        await Run(() => s.MutateMapsAsync(maps => { EnsureMapSnapshot(maps, snapshot); maps.RemoveAt(index); return maps; }));
    }
    private async void MapUp_Click(object sender, RoutedEventArgs e) => await MoveMap(-1);
    private async void MapDown_Click(object sender, RoutedEventArgs e) => await MoveMap(1);
    private async Task MoveMap(int delta)
    {
        var s = selected; if (s is null) return; var index = CurrentMapsList.SelectedIndex; var target = index + delta;
        var snapshot = s.Profile.DesiredMaps?.ToArray() ?? s.MapSnapshot(); if (index < 0 || target < 0 || target >= snapshot.Length) return;
        await Run(async () => { await s.MutateMapsAsync(maps => { EnsureMapSnapshot(maps, snapshot); (maps[index], maps[target]) = (maps[target], maps[index]); return maps; }); if (selected == s) CurrentMapsList.SelectedIndex = target; });
    }
    private async void SetNextMap_Click(object sender, RoutedEventArgs e)
    {
        var s = selected; var index = CurrentMapsList.SelectedIndex;
        if (s is not null && index >= 0) await Run(() => s.SetNextMapAsync(index, CurrentMapsList.Items.Cast<MapEntry>().ToArray()));
    }
    private bool mapSwitchInProgress;
    private async void CurrentMaps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || mapSwitchInProgress || selected is not { } s ||
            Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: MapEntry } row ||
            ItemsControl.ItemsControlFromItemContainer(row) != CurrentMapsList) return;
        var index = CurrentMapsList.ItemContainerGenerator.IndexFromContainer(row);
        if (index < 0) return;
        var snapshot = CurrentMapsList.Items.Cast<MapEntry>().ToArray();
        e.Handled = true; dragItem = null; mapSwitchInProgress = true;
        try { await Run(() => s.SwitchMapAsync(index, snapshot)); }
        finally { mapSwitchInProgress = false; }
    }
    private void MapDrag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this); dragItem = null;
        if (Ancestor<Button>(e.OriginalSource as DependencyObject) is not null || Ancestor<ComboBox>(e.OriginalSource as DependencyObject) is not null) return;
        var element = e.OriginalSource as FrameworkElement;
        dragItem = sender == CurrentMapsList ? Ancestor<ListBoxItem>(element)?.DataContext : Ancestor<ContentPresenter>(element)?.DataContext;
        if (sender == CurrentMapsList && Ancestor<ListBoxItem>(element) is { } row) CurrentMapsList.SelectedItem = row.DataContext;
    }
    private void AvailableMaps_MouseMove(object sender, MouseEventArgs e) { if (dragItem is ModeChoice mode && ShouldDrag(e)) { dragItem = null; DragDrop.DoDragDrop(AvailableMapsList, new DataObject("LightRcon.NewMap", mode.Entry), DragDropEffects.Copy); } }
    private void CurrentMaps_MouseMove(object sender, MouseEventArgs e) { if (dragItem is MapEntry && ShouldDrag(e)) { var index = CurrentMapsList.SelectedIndex; dragItem = null; if (index >= 0) DragDrop.DoDragDrop(CurrentMapsList, new DataObject("LightRcon.MapIndex", index), DragDropEffects.Move); } }
    private bool ShouldDrag(MouseEventArgs e) => e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - dragStart).Length > 8;
    private async void CurrentMaps_Drop(object sender, DragEventArgs e)
    {
        var s = selected; if (s is null) return;
        var snapshot = s.Profile.DesiredMaps?.ToArray() ?? s.MapSnapshot();
        var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject); var target = item is null ? CurrentMapsList.Items.Count : CurrentMapsList.ItemContainerGenerator.IndexFromContainer(item);
        var entry = e.Data.GetData("LightRcon.NewMap") as MapEntry; var index = e.Data.GetData("LightRcon.MapIndex") as int?;
        e.Handled = true;
        await Run(async () =>
        {
            await s.MutateMapsAsync(maps =>
            {
                EnsureMapSnapshot(maps, snapshot); target = Math.Clamp(target, 0, maps.Count);
                if (entry is not null) maps.Insert(target, entry);
                else if (index is int from && from >= 0 && from < maps.Count) { var map = maps[from]; maps.RemoveAt(from); if (target > from) target--; maps.Insert(Math.Min(target, maps.Count), map); }
                return maps;
            });
        });
    }
    private static void EnsureMapSnapshot(List<MapEntry> maps, MapEntry[] expected)
    {
        if (!maps.SequenceEqual(expected)) throw new ArgumentException(UiText.Get("The map list changed. Select the map again."));
    }
    private void UpdateMapGroups()
    {
        var game = selected?.Game ?? GameId.BF4;
        availableMaps.Clear();
        availableMaps.AddRange(MapCatalog.For(game).Select(x => new MapChoice(x.FileName, x.Name, x.Modes.Select(m => new ModeChoice(x.FileName, m.Code, m.Name) { Game = game }).ToList())));
        var query = MapSearch.Text.Trim();
        AvailableMapsList.ItemsSource = availableMaps.Where(x => $"{x.Name} {x.FileName}".Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => MapCatalog.Expansion(x.FileName, game)).OrderBy(x => MapCatalog.ExpansionOrder(x.Key))
            .Select(x => new MapGroup(UiText.Get(x.Key), x.OrderBy(m => m.Name).ToList())).ToArray();
    }
}
