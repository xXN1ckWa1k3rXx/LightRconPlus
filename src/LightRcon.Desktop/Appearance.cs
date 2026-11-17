using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace LightRcon.Desktop;

public partial class MainWindow
{
    internal bool IsDarkTheme { get; private set; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void InitializeAppearance()
    {
        ThemeSelector.SelectedValue = store.State.Theme is "light" or "dark" ? store.State.Theme : "system";
        ApplyAppearance();
        SourceInitialized += (_, _) => ApplyTitleBar();
        SystemEvents.UserPreferenceChanged += SystemAppearanceChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= SystemAppearanceChanged;
    }
    private void SystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (store.State.Theme == "system" && !closing && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ApplyAppearance);
    }
    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || closing || ThemeSelector.SelectedValue is not string code) return;
        store.State.Theme = code; store.Save(); ApplyAppearance();
    }
    private void ApplyAppearance()
    {
        var systemDark = false;
        try { systemDark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException) { }
        IsDarkTheme = store.State.Theme == "dark" || store.State.Theme == "system" && systemDark;
        string[] keys = ["Background", "Surface", "Raised", "Line", "Text", "Muted", "Accent", "Selection", "Hover", "Sidebar", "Danger", "Warning", "WarningText", "Team1", "Team2", "Team3", "Team4", "Online", "Offline"];
        string[] light = ["#F4F4F4", "#FFFFFF", "#ECECEC", "#BDBDBD", "#202020", "#606060", "#1769AA", "#DCEBFA", "#E7E7E7", "#F9F9F9", "#B3261E", "#FFF4CE", "#694F00", "#1476B8", "#C74932", "#338050", "#8C57AC", "#27823B", "#CC482D"];
        string[] dark = ["#202020", "#292929", "#333333", "#565656", "#EEEEEE", "#B8B8B8", "#69B9F0", "#244B67", "#3A3A3A", "#252525", "#FF928A", "#463C21", "#F5D985", "#65BEFA", "#FF977C", "#85D697", "#CBA0F5", "#75D88A", "#FF8A73"];
        var palette = IsDarkTheme ? dark : light;
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
        Application.Current.Resources["DeadPlayer"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDarkTheme ? "#593036" : "#F8D7DA"));
        ApplyTitleBar();
    }
    private void ApplyTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var value = IsDarkTheme ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0) _ = DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
    }
}
