using System.Security.Cryptography;
using System.Text;
using LightRcon.Core;

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
try
{
    foreach (var profile in profiles)
    {
        var session = new ServerSession(profile, store); sessions.Add(session);
        ConnectionState previous = (ConnectionState)(-1);
        session.Changed += () =>
        {
            if (previous == session.State) return; previous = session.State;
            System.Console.WriteLine($"[{DateTime.Now:T}] [{profile.Name}] {session.Status}");
        };
        if (profile.Layer.Enabled)
        {
            await session.StartLayerAsync(); System.Console.WriteLine($"[{profile.Name}] Layer {profile.Layer.BindAddress}:{profile.Layer.Port}");
        }
        await session.StartAsync();
    }
    System.Console.WriteLine("LightRcon+ host is running. Ctrl+C to stop.");
    await stop.Task;
}
finally
{
    foreach (var session in sessions) await session.DisposeAsync(); store.Save();
}
return 0;
