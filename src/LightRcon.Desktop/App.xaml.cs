using System.Configuration;
using System.Data;
using System.Windows;
using LightRcon.Core;

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LightRcon.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? mutex;
    public static string? DataRoot { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        if (Environment.OSVersion.Version.Build < 17763) { AppDialog.Alert(null, UiText.Get("Windows Server 2019 / Windows 10 1809 or later is required.")); Shutdown(); return; }
        var index = Array.IndexOf(e.Args, "--data-dir");
        if (index >= 0 && index + 1 < e.Args.Length) DataRoot = Path.GetFullPath(e.Args[index + 1]);
        var root = DataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightRconPlus");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
        mutex = new Mutex(true, "Local\\LightRconPlus-" + key, out var created);
        if (!created) { AppDialog.Alert(null, UiText.Get("This data directory is already in use by LightRcon+ desktop or console.")); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { AppDialog.Alert(Current.MainWindow, args.Exception.Message); args.Handled = true; };
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
