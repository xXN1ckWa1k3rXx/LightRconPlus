using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using LightRcon.Core;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    private MapInsertionAdorner? mapInsertion;
    private void ClearMapInsertion()
    {
        if (mapInsertion is null) return;
        AdornerLayer.GetAdornerLayer(CurrentMapsList)?.Remove(mapInsertion);
        mapInsertion = null;
    }
    internal static int MapInsertionIndex(int rowIndex, double position, double rowHeight, int count)
        => rowIndex < 0 ? count : Math.Clamp(rowIndex + (position >= rowHeight / 2 ? 1 : 0), 0, count);
    internal int MapDropIndex(Point point)
    {
        for (var index = 0; index < CurrentMapsList.Items.Count; index++)
            if (CurrentMapsList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row)
            {
                var top = row.TranslatePoint(new Point(), CurrentMapsList).Y;
                if (point.Y <= top + row.ActualHeight) return MapInsertionIndex(index, point.Y - top, row.ActualHeight, CurrentMapsList.Items.Count);
            }
        return CurrentMapsList.Items.Count;
    }
    private void CurrentMaps_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var canDrop = selected is { IsConnected: true } session && session.Permissions.HasFlag(LayerPermission.Maps);
        e.Effects = !canDrop ? DragDropEffects.None : e.Data.GetDataPresent("LightRcon.NewMap") ? DragDropEffects.Copy
            : e.Data.GetData("LightRcon.MapIndex") is int from && from >= 0 && from < CurrentMapsList.Items.Count ? DragDropEffects.Move : DragDropEffects.None;
        if (e.Effects == DragDropEffects.None) { ClearMapInsertion(); return; }
        var point = e.GetPosition(CurrentMapsList);
        var scroll = FindChild<ScrollViewer>(CurrentMapsList);
        if (point.Y < 22) scroll?.LineUp();
        else if (point.Y > CurrentMapsList.ActualHeight - 22) scroll?.LineDown();
        var target = MapDropIndex(point);
        var y = 4d;
        if (CurrentMapsList.Items.Count > 0)
        {
            var index = Math.Min(target, CurrentMapsList.Items.Count - 1);
            if (CurrentMapsList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row)
                y = row.TranslatePoint(new Point(0, target == CurrentMapsList.Items.Count ? row.ActualHeight : 0), CurrentMapsList).Y;
        }
        if (mapInsertion is null && AdornerLayer.GetAdornerLayer(CurrentMapsList) is { } layer)
        {
            mapInsertion = new(CurrentMapsList) { IsHitTestVisible = false }; layer.Add(mapInsertion);
        }
        if (mapInsertion is not null) { mapInsertion.Position = Math.Clamp(y, 2, Math.Max(2, CurrentMapsList.ActualHeight - 2)); mapInsertion.InvalidateVisual(); }
    }
    private void CurrentMaps_DragLeave(object sender, DragEventArgs e) => ClearMapInsertion();
    private sealed class MapInsertionAdorner(UIElement element) : Adorner(element)
    {
        public double Position { get; set; }
        protected override void OnRender(DrawingContext context)
        {
            var brush = (Brush)((FrameworkElement)AdornedElement).FindResource("Accent");
            context.DrawLine(new Pen(brush, 3), new(3, Position), new(Math.Max(3, AdornedElement.RenderSize.Width - 3), Position));
            context.DrawEllipse(brush, null, new(5, Position), 4, 4);
        }
    }
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
    private void AvailableMaps_MouseMove(object sender, MouseEventArgs e) { if (dragItem is ModeChoice mode && ShouldDrag(e)) { dragItem = null; try { DragDrop.DoDragDrop(AvailableMapsList, new DataObject("LightRcon.NewMap", mode.Entry), DragDropEffects.Copy); } finally { ClearMapInsertion(); } } }
    private void CurrentMaps_MouseMove(object sender, MouseEventArgs e) { if (dragItem is MapEntry && ShouldDrag(e)) { var index = CurrentMapsList.SelectedIndex; dragItem = null; try { if (index >= 0) DragDrop.DoDragDrop(CurrentMapsList, new DataObject("LightRcon.MapIndex", index), DragDropEffects.Move); } finally { ClearMapInsertion(); } } }
    private bool ShouldDrag(MouseEventArgs e) => e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - dragStart).Length > 8;
    private async void CurrentMaps_Drop(object sender, DragEventArgs e)
    {
        ClearMapInsertion();
        var s = selected; if (s is null) return;
        var snapshot = s.Profile.DesiredMaps?.ToArray() ?? s.MapSnapshot();
        var target = MapDropIndex(e.GetPosition(CurrentMapsList));
        var entry = e.Data.GetData("LightRcon.NewMap") as MapEntry; var index = e.Data.GetData("LightRcon.MapIndex") as int?;
        e.Handled = true;
        if (entry is null && index is null) return;
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
