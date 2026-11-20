using System.Security.Cryptography;
using System.Text;
using LightRcon.Core;
using LightRcon.Host;

if (!OperatingSystem.IsWindows() || Environment.OSVersion.Version.Build < 17763)
{
    System.Console.Error.WriteLine("Requires Windows Server 2019 / Windows 10 1809 or later."); return 1;
}
HostOptions options;
try { options = HostOptions.Parse(args); }
catch (ArgumentException ex) { System.Console.Error.WriteLine(ex.Message); return 2; }
if (options.ShowHelp) { System.Console.WriteLine(HostOptions.Help); return 0; }
var root = options.Get("--data-dir") is { } custom ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightRconPlus");
var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
using var mutex = new Mutex(true, "Local\\LightRconPlus-" + key, out var created);
if (!created) { System.Console.Error.WriteLine("This data directory is already in use by LightRcon+."); return 2; }
var store = new ProfileStore(root);
System.Console.WriteLine($"Data directory: {store.Root}");
if (options.List)
{
    foreach (var p in store.State.Servers) System.Console.WriteLine($"{p.Id}  {p.DisplayName}  Layer={p.Layer.Enabled}"); return 0;
}
ServerProfile? bootstrap = null;
if (options.Get("--connect") is not null)
{
    try { bootstrap = options.Bootstrap(store); }
    catch (ArgumentException ex) { System.Console.Error.WriteLine(ex.Message); return 2; }
    System.Console.WriteLine($"Saved profile {bootstrap.Id}: {bootstrap.DisplayName}");
    if (options.ConfigureOnly) return 0;
}
var filter = options.Get("--server");
var profiles = store.State.Servers.Where(x => bootstrap is not null ? x.Id == bootstrap.Id : filter is null || x.Id.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
if (profiles.Length == 0) { System.Console.Error.WriteLine("No matching profiles. Use --connect to configure one; see --help."); return 2; }
var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
System.Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
List<ServerSession> sessions = [];
using var keyboardCancellation = new CancellationTokenSource();
Task keyboard = Task.CompletedTask;
try
{
    foreach (var profile in profiles)
    {
        var session = new ServerSession(profile, store, TimeSpan.FromSeconds(5)); sessions.Add(session);
        var reporter = new HostStatusReporter(session, System.Console.WriteLine, System.Console.Error.WriteLine);
        session.Changed += reporter.Report;
        session.Logged += entry =>
        {
            if (entry.IsPluginConsole || entry.Kind == "Layer") System.Console.WriteLine($"[{entry.Timestamp:T}] [{profile.Name}] [{profile.Id}] [{entry.Kind}] {entry.Text}");
        };
        if (profile.Layer.Enabled)
        {
            await session.StartLayerAsync(); System.Console.WriteLine($"[{profile.Name}] Layer {profile.Layer.BindAddress}:{profile.Layer.Port}");
            System.Console.WriteLine($"[{profile.Name}] [{profile.Id}] Layer accounts: {profile.Layer.Accounts.Count} total, {profile.Layer.Accounts.Count(account => account.Enabled)} enabled.");
        }
        await session.StartAsync();
    }
    keyboard = HostConsole.ReadKeysAsync(sessions, HostConsole.ReadKey, System.Console.WriteLine, keyboardCancellation.Token);
    System.Console.WriteLine(System.Console.IsInputRedirected
        ? "LightRcon+ host is running. Automatic retry: 5 seconds. Ctrl+C to stop. F3 requires an interactive console."
        : "LightRcon+ host is running. Automatic retry: 5 seconds. F3 to reconnect all profiles; Ctrl+C to stop.");
    await stop.Task;
}
finally
{
    keyboardCancellation.Cancel();
    await keyboard;
    foreach (var session in sessions) await session.DisposeAsync(); store.Save();
}
return 0;
