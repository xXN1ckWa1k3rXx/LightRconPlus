using System.Threading.Channels;

namespace LightRcon.Core.Plugins;

public sealed class PluginManager
{
    private sealed class Running : IAsyncDisposable
    {
        private readonly IPluginRuntime runtime;
        private readonly Channel<Func<Task>> queue = Channel.CreateBounded<Func<Task>>(512);
        private readonly Task worker;
        private readonly CancellationTokenSource ticking = new();
        private readonly Task clock;
        public bool Enabled { get; private set; }
        public Running(IPluginRuntime runtime, Action<Exception> error)
        {
            this.runtime = runtime;
            worker = Task.Run(async () => { await foreach (var operation in queue.Reader.ReadAllAsync()) await operation(); });
            // Native plugins have no Tick work. Do not keep ten queue entries per
            // second and a timer alive for every native plugin.
            clock = runtime is not LuaPlugin ? Task.CompletedTask : Task.Run(async () => {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                try { while (await timer.WaitForNextTickAsync(ticking.Token)) if (Enabled) Post(() => runtime.Tick(), error); }
                catch (OperationCanceledException) { }
            });
        }
        public async Task<T> Invoke<T>(Func<IPluginRuntime, T> operation)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            await queue.Writer.WriteAsync(() => {
                try { completion.SetResult(operation(runtime)); } catch (Exception error) { completion.SetException(error); }
                return Task.CompletedTask;
            });
            return await completion.Task;
        }
        public Task Enable() => Invoke(runtime => { runtime.Enable(); Enabled = true; return true; });
        public void Dispatch(string[] words, Action<Exception> error) { if (Enabled) Post(() => runtime.Handle((string[])words.Clone()), error); }
        private void Post(Action operation, Action<Exception> error)
        {
            if (!queue.Writer.TryWrite(() => { try { operation(); } catch (Exception failure) { error(failure); } return Task.CompletedTask; }))
                error(new IOException(UiText.Get("Plugin event queue is full.")));
        }
        public async ValueTask DisposeAsync()
        {
            Enabled = false;
            ticking.Cancel(); await clock; ticking.Dispose();
            queue.Writer.TryComplete(); await worker;
            await runtime.DisposeAsync();
        }
    }
    private readonly ServerSession session;
    private readonly ProfileStore store;
    private readonly SemaphoreSlim changes = new(1);
    private readonly Dictionary<string, Running> active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginDetails> details = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginSetting[]> cachedSettings = new(StringComparer.OrdinalIgnoreCase);
    private long settingsRevision;
    private ProconPlugin? procon;
    private ProconPlugin? failedProcon;
    private readonly HashSet<string> proconPaths = new(StringComparer.OrdinalIgnoreCase);
    private PluginDetails[] remotePlugins = [];
    private string? libraryStamp;
    private readonly HashSet<string> libraryPaths = new(StringComparer.OrdinalIgnoreCase);
    public bool IsRestoring { get; private set; }
    public event Action? Changed;
    internal void AcceptRemoteSnapshot(PluginDetails[] plugins)
    {
        lock (details)
        {
            foreach (var old in remotePlugins)
                if (!plugins.Contains(old)) cachedSettings.Remove(old.Path);
            remotePlugins = plugins;
        }
        Changed?.Invoke();
    }
    internal PluginManager(ServerSession session, ProfileStore store)
    {
        this.session = session; this.store = store;
        foreach (var path in session.Profile.EnabledPlugins)
            if (!session.Profile.InstalledPlugins.Contains(path, StringComparer.OrdinalIgnoreCase)) session.Profile.InstalledPlugins.Add(path);
    }
    public bool CompatibilityEnabled => Snapshot().Any(item => item.Kind == "Procon" && item.Enabled);
    public string[] Enabled { get { if (session.Profile.IsLayer) return Snapshot().Where(item => item.Enabled).Select(item => item.Path).ToArray(); lock (session.Profile.Sync) return session.Profile.EnabledPlugins.ToArray(); } }
    public PluginDetails[] Snapshot()
    {
        if (session.Profile.IsLayer) { lock (details) return remotePlugins.ToArray(); }
        lock (session.Profile.Sync)
        lock (details)
            return session.Profile.InstalledPlugins.Select(path =>
            {
                var value = details.GetValueOrDefault(path) ?? new(path, Path.GetFileNameWithoutExtension(path), "", "", "", "", "");
                return IsRestoring && value.Error.Length == 0 ? value with { Enabled = session.Profile.EnabledPlugins.Contains(path, StringComparer.OrdinalIgnoreCase) } : value;
            })
                .ToArray();
    }
    public async Task ImportAsync(string path)
    {
        if (session.Profile.IsLayer) throw new InvalidOperationException(UiText.Get("Add plugins on the upstream Layer."));
        path = Path.GetFullPath(path);
        await changes.WaitAsync();
        try { Register(path); await Load(path); }
        finally { changes.Release(); Changed?.Invoke(); }
    }
    private void Register(string path)
    {
        lock (session.Profile.Sync)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (File.Exists(path) && extension is ".cs" or ".dll" && !NativePlugin.IsNative(path))
            {
                PublishKind(path, "Procon");
                var replaced = session.Profile.InstalledPlugins.Where(candidate => !candidate.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(candidate).Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)
                    && Path.GetExtension(candidate).ToLowerInvariant() is ".cs" or ".dll").ToArray();
                foreach (var candidate in replaced)
                {
                    session.Profile.InstalledPlugins.Remove(candidate);
                    if (session.Profile.EnabledPlugins.Remove(candidate) && !session.Profile.EnabledPlugins.Contains(path)) session.Profile.EnabledPlugins.Add(path);
                    if (session.Profile.PluginSettings.Remove(candidate, out var saved) && !session.Profile.PluginSettings.ContainsKey(path)) session.Profile.PluginSettings[path] = saved;
                    lock (details) details.Remove(candidate);
                }
            }
            if (!session.Profile.InstalledPlugins.Contains(path, StringComparer.OrdinalIgnoreCase)) session.Profile.InstalledPlugins.Add(path);
        }
        store.Save();
    }
    private Dictionary<string, string> Saved(string path)
    {
        lock (session.Profile.Sync) return new(session.Profile.PluginSettings.GetValueOrDefault(path) ?? [], StringComparer.Ordinal);
    }
    private void Publish(PluginDetails value) { lock (details) details[value.Path] = value with { SettingsRevision = details.GetValueOrDefault(value.Path)?.SettingsRevision ?? 0 }; }
    private void PublishKind(string path, string kind)
    {
        lock (details)
        {
            var value = details.GetValueOrDefault(path) ?? new(path, Path.GetFileNameWithoutExtension(path), "", "", "", "", kind);
            details[path] = value with { Kind = kind };
        }
    }
    private bool HasRuntime(string path) { lock (active) return active.ContainsKey(path) || proconPaths.Contains(path) && procon is { IsAlive: true }; }
    private async Task Load(string path)
    {
        if (HasRuntime(path)) return;
        try
        {
            if (!session.IsConnected) throw new IOException(UiText.Get("Connect before loading plugins."));
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".lua" or ".cs" or ".dll")) throw new InvalidDataException(UiText.Get("Select a .cs, .dll or .lua plugin."));
            if (extension != ".lua" && !NativePlugin.IsNative(path))
            {
                PublishKind(path, "Procon");
                await RebuildProcon(path);
                return;
            }
            var runtime = await Task.Run<IPluginRuntime>(() => extension == ".lua"
                ? new LuaPlugin(path, session, Path.Combine(store.ServerDirectory(session.Profile.Id), "plugins", "lua", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..16]))
                : new NativePlugin(path, session));
            Running? running = null;
            try
            {
                running = new(runtime, error => RecordError(path, error));
                foreach (var setting in Saved(path)) await running.Invoke(plugin => { plugin.SetSetting(setting.Key, setting.Value); return true; });
                lock (active) active.Add(path, running);
                Publish((await running.Invoke(plugin => plugin.Details)) with { Enabled = running.Enabled });
                CacheSettings(path, await running.Invoke(plugin => plugin.GetSettings().ToArray()));
            }
            catch { if (running is not null) await running.DisposeAsync(); else await runtime.DisposeAsync(); throw; }
        }
        catch (Exception error) { RecordError(path, error); throw; }
    }
    private void RecordError(string path, Exception error)
    {
        var message = error.GetBaseException().Message;
        if (Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase)) message = PluginDiagnostics.Redact(message);
        lock (details)
        {
            var value = details.GetValueOrDefault(path) ?? new(path, Path.GetFileNameWithoutExtension(path), "", "", "", "", "");
            details[path] = value with { Error = message };
        }
        session.Log("Plugin", Path.GetFileName(path) + ": " + message);
    }
    private async Task RebuildProcon(string path)
    {
        string[] paths;
        lock (session.Profile.Sync)
            paths = session.Profile.InstalledPlugins.Where(candidate => Path.GetExtension(candidate).ToLowerInvariant() is ".cs" or ".dll")
                .Where(File.Exists).Where(candidate => !NativePlugin.IsNative(candidate)).GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last()).ToArray();
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException(UiText.Get("A plugin with this class name is already installed."));
        if (procon is not null) { await procon.DisposeAsync(); procon = null; }
        lock (active) proconPaths.Clear();
        procon = await ProconPlugin.Start(paths, store, session);
        Exception? requestedError = null;
        foreach (var candidate in paths)
        {
            try
            {
                if (procon.StagingErrors.TryGetValue(candidate, out var stagingError)) throw stagingError;
                await procon.Request("load", Path.GetFileNameWithoutExtension(candidate));
                foreach (var setting in Saved(candidate)) await procon.Request("set", Path.GetFileNameWithoutExtension(candidate), setting.Key, setting.Value);
                var enabled = Enabled.Contains(candidate, StringComparer.OrdinalIgnoreCase);
                if (enabled) await procon.Request("enable", Path.GetFileNameWithoutExtension(candidate));
                Publish((await procon.Details(candidate)) with { Enabled = enabled });
                CacheSettings(candidate, await procon.Settings(candidate));
                lock (active) proconPaths.Add(candidate);
            }
            catch (Exception error) { RecordError(candidate, error); if (candidate.Equals(path, StringComparison.OrdinalIgnoreCase)) requestedError = error; }
        }
        if (requestedError is not null) throw new InvalidDataException(requestedError.Message, requestedError);
    }
    public async Task EnableAsync(string path)
    {
        if (session.Profile.IsLayer) { await RemoteChange("plugin.enable", path); return; }
        path = Path.GetFullPath(path);
        await changes.WaitAsync();
        try
        {
            Register(path); await Load(path);
            Running? running;
            lock (active) active.TryGetValue(path, out running);
            if (running is not null) { if (!running.Enabled) await running.Enable(); }
            else if (!Snapshot().Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && item.Enabled))
            {
                await procon!.SyncAccountsAsync();
                await procon.Request("enable", Path.GetFileNameWithoutExtension(path));
            }
            lock (session.Profile.Sync) if (!session.Profile.EnabledPlugins.Contains(path, StringComparer.OrdinalIgnoreCase)) session.Profile.EnabledPlugins.Add(path);
            lock (details) details[path] = details[path] with { Enabled = true, Error = "" };
            CacheSettings(path, running is not null ? await running.Invoke(plugin => plugin.GetSettings().ToArray()) : await procon!.Settings(path));
            store.Save(); session.Log("Plugin", UiText.Format("Enabled plugin: {0}", Path.GetFileName(path)));
        }
        catch (Exception error) { RecordError(path, error); throw; }
        finally { changes.Release(); Changed?.Invoke(); }
    }
    public async Task DisableAsync(string path)
    {
        if (session.Profile.IsLayer) { await RemoteChange("plugin.disable", path); return; }
        await changes.WaitAsync();
        try
        {
            Running? running;
            lock (active) active.Remove(path, out running);
            if (running is not null) await running.DisposeAsync();
            else if (proconPaths.Contains(path)) await procon!.Request("disable", Path.GetFileNameWithoutExtension(path));
            if (proconPaths.Contains(path)) CacheSettings(path, await procon!.Settings(path));
            lock (session.Profile.Sync) session.Profile.EnabledPlugins.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            lock (details) if (details.TryGetValue(path, out var value)) details[path] = value with { Enabled = false };
            store.Save(); session.Log("Plugin", UiText.Format("Disabled plugin: {0}", Path.GetFileName(path)));
        }
        finally { changes.Release(); Changed?.Invoke(); }
    }
    public async Task<PluginDetails> GetDetailsAsync(string path)
    {
        if (session.Profile.IsLayer)
            return (await session.RequestPluginsAsync("plugin.details", path)).Plugin ?? throw new IOException(UiText.Get("Plugin failed"));
        await changes.WaitAsync();
        try
        {
            if (!HasRuntime(path))
                lock (details)
                    if (details.TryGetValue(path, out var cached) && (cached.Error.Length > 0 || cachedSettings.ContainsKey(path))) return cached;
            await Load(path);
            if (proconPaths.Contains(path))
            {
                var value = await procon!.Details(path);
                lock (details) Publish(value with { Enabled = details.GetValueOrDefault(path)?.Enabled == true });
            }
            else
            {
                Running running; lock (active) running = active[path];
                var value = await running.Invoke(plugin => plugin.Details);
                Publish(value with { Enabled = running.Enabled });
            }
            lock (details) return details[path];
        }
        finally { changes.Release(); }
    }
    public async Task RefreshAsync()
    {
        if (session.Profile.IsLayer) { await session.RequestPluginsAsync("plugin.list"); return; }
        await ScanLibraryAsync(force: true);
        var externalProcon = Snapshot().FirstOrDefault(item => item.Kind == "Procon" && !libraryPaths.Contains(item.Path));
        if (externalProcon is not null && !Snapshot().Any(item => item.Kind == "Procon" && libraryPaths.Contains(item.Path)))
        {
            await changes.WaitAsync();
            try { await RebuildProcon(externalProcon.Path); }
            finally { changes.Release(); }
        }
        foreach (var path in Snapshot().Select(item => item.Path))
            try
            {
                if (!HasRuntime(path) && !libraryPaths.Contains(path) && Snapshot().Any(item => item.Path == path && item.Error.Length > 0)) await ImportAsync(path);
                else { await GetDetailsAsync(path); await GetSettingsAsync(path); }
            }
            catch (Exception error) { RecordError(path, error); }
        Changed?.Invoke();
    }
    public async Task ScanLibraryAsync(bool force = false)
    {
        if (session.Profile.IsLayer || !session.IsConnected) return;
        await changes.WaitAsync(session.LifetimeToken);
        var scanned = false;
        try
        {
            if (!session.IsConnected) return;
            var stamp = PluginLibrary.Stamp(session.PluginDirectory, session.Game);
            if (!force && stamp == libraryStamp) return;
            var paths = PluginLibrary.Discover(session.PluginDirectory, session.Game);
            var initialized = libraryStamp is not null;
            var changed = stamp != libraryStamp;
            libraryStamp = stamp;
            scanned = true;
            libraryPaths.Clear(); libraryPaths.UnionWith(paths);
            foreach (var path in paths) Register(path);
            session.Log("Plugin", UiText.Format("Scanning plugin library: {0}", Path.Combine(session.PluginDirectory, PluginLibrary.GameFolder(session.Game))));
            var proconToLoad = new List<string>();
            foreach (var path in paths)
            {
                session.LifetimeToken.ThrowIfCancellationRequested();
                try
                {
                    var legacy = Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".dll" && !NativePlugin.IsNative(path);
                    if (legacy)
                    {
                        if (!HasRuntime(path) || initialized && (changed || force)) proconToLoad.Add(path);
                        continue;
                    }
                    var enabled = Enabled.Contains(path, StringComparer.OrdinalIgnoreCase);
                    if (initialized && changed)
                    {
                        Running? running; lock (active) active.Remove(path, out running);
                        if (running is not null) await running.DisposeAsync();
                    }
                    await Load(path);
                    if (enabled)
                    {
                        Running running; lock (active) running = active[path];
                        if (!running.Enabled) await running.Enable();
                        lock (details) details[path] = details[path] with { Enabled = true, Error = "" };
                    }
                }
                catch (OperationCanceledException) when (session.LifetimeToken.IsCancellationRequested) { throw; }
                catch (Exception error) { RecordError(path, error); }
            }
            if (proconToLoad.Count > 0)
            {
                try { await RebuildProcon(proconToLoad[0]); }
                catch (OperationCanceledException) when (session.LifetimeToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    foreach (var path in proconToLoad)
                        if (!HasRuntime(path) && !Snapshot().Any(item => item.Path == path && item.Error.Length > 0)) RecordError(path, error);
                }
            }
            store.Save();
        }
        finally { changes.Release(); if (scanned) Changed?.Invoke(); }
    }
    public async Task<PluginSetting[]> GetSettingsAsync(string path)
    {
        if (session.Profile.IsLayer)
        {
            var remote = (await session.RequestPluginsAsync("plugin.settings", path)).PluginSettings;
            CacheSettings(path, remote);
            return remote;
        }
        await changes.WaitAsync();
        try
        {
            if (!HasRuntime(path))
                lock (details)
                    if (details.TryGetValue(path, out var failed) && failed.Error.Length > 0) return [];
            if (!HasRuntime(path))
                lock (details)
                    if (cachedSettings.TryGetValue(path, out var cached)) return cached.ToArray();
            await Load(path);
            PluginSetting[] settings;
            if (proconPaths.Contains(path)) settings = await procon!.Settings(path);
            else
            {
                Running running; lock (active) running = active[path];
                settings = await running.Invoke(plugin => plugin.GetSettings().ToArray());
            }
            CacheSettings(path, settings);
            return settings;
        }
        finally { changes.Release(); }
    }
    private void CacheSettings(string path, PluginSetting[] settings)
    {
        lock (details)
        {
            if (!session.Profile.IsLayer && (!cachedSettings.TryGetValue(path, out var previous) || !previous.SequenceEqual(settings)) && details.TryGetValue(path, out var value))
                details[path] = value with { SettingsRevision = ++settingsRevision };
            cachedSettings[path] = settings.ToArray();
        }
    }
    public async Task<(PluginDetails Details, PluginSetting[] Settings)> GetViewAsync(string path, bool refresh = false)
    {
        lock (details)
        {
            var value = session.Profile.IsLayer ? remotePlugins.FirstOrDefault(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) : details.GetValueOrDefault(path);
            if (!refresh && value is not null)
            {
                if (value.Error.Length > 0) return (value, []);
                if (cachedSettings.TryGetValue(path, out var settings)) return (value, settings.ToArray());
            }
        }
        return (await GetDetailsAsync(path), await GetSettingsAsync(path));
    }
    public async Task SetSettingAsync(string path, string name, string value)
    {
        if (session.Profile.IsLayer) { await RemoteChange("plugin.set", path, name, value); return; }
        await changes.WaitAsync();
        try
        {
            await Load(path);
            PluginSetting[] settings;
            Running? running; lock (active) active.TryGetValue(path, out running);
            settings = running is not null ? await running.Invoke(plugin => plugin.GetSettings().ToArray()) : await procon!.Settings(path);
            var setting = settings.Single(item => item.Name == name);
            if (setting.ReadOnly) throw new InvalidOperationException(UiText.Get("This setting is read-only."));
            Validate(setting, value);
            // Array encoding can differ while the lines stay identical; reapplying
            // such values can restart a plugin's timers (for example CSpambot).
            if (setting.Type.Equals("stringarray", StringComparison.OrdinalIgnoreCase) && setting.MatchesValue(value)) { CacheSettings(path, settings); return; }
            if (running is not null) await running.Invoke(plugin => { plugin.SetSetting(name, value); return true; });
            else await procon!.Request("set", Path.GetFileNameWithoutExtension(path), name, value);
            CacheSettings(path, running is not null ? await running.Invoke(plugin => plugin.GetSettings().ToArray()) : await procon!.Settings(path));
            lock (session.Profile.Sync)
            {
                if (!session.Profile.PluginSettings.TryGetValue(path, out var saved)) session.Profile.PluginSettings[path] = saved = [];
                saved[name] = value;
            }
            store.Save(); session.Log("Plugin", UiText.Get("Plugin settings saved."));
        }
        finally { changes.Release(); Changed?.Invoke(); }
    }
    private async Task RemoteChange(string operation, params string[] arguments)
    {
        // Ordered push frames are authoritative. A response continuation may run
        // after a newer push has already arrived from another administrator.
        await session.RequestPluginsAsync(operation, arguments);
        lock (details) if (arguments.Length > 0) cachedSettings.Remove(arguments[0]);
    }
    internal static void Validate(PluginSetting setting, string value)
    {
        var type = setting.Type.ToLowerInvariant();
        if (setting.Choices.Length > 0 && !setting.Choices.Contains(value) ||
            type is "bool" or "boolean" && !bool.TryParse(value, out _) ||
            type is "int" or "int32" && !int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _) ||
            type is "double" or "float" or "single" && (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)))
            throw new ArgumentException(UiText.Get("Invalid plugin setting value."));
    }
    internal async Task RestoreAsync()
    {
        IsRestoring = true;
        try
        {
        if (session.Profile.IsLayer)
        {
            // The initial list is pushed before this request can be acknowledged.
            await session.RequestPluginsAsync("plugin.list");
            foreach (var plugin in Snapshot().Where(item => item.Kind.Length > 0 && item.Error.Length == 0))
                try { await GetSettingsAsync(plugin.Path); }
                catch (Exception error) when (error is not OperationCanceledException) { session.Log("Plugin", error.Message); }
            return;
        }
        try { await ScanLibraryAsync(); }
        catch (OperationCanceledException) when (session.LifetimeToken.IsCancellationRequested) { throw; }
        catch (Exception error) { session.Log("Plugin", UiText.Format("Plugin library scan failed: {0}", error.Message)); }
        string[] paths; lock (session.Profile.Sync) paths = session.Profile.InstalledPlugins.ToArray();
        foreach (var path in paths)
        {
            session.LifetimeToken.ThrowIfCancellationRequested();
            if (libraryPaths.Contains(path) && Snapshot().Any(item => item.Path == path && item.Error.Length > 0)) continue;
            try { if (Enabled.Contains(path, StringComparer.OrdinalIgnoreCase)) await EnableAsync(path); else await ImportAsync(path); }
            catch (OperationCanceledException) when (session.LifetimeToken.IsCancellationRequested) { throw; }
            catch (Exception error) { RecordError(path, error); }
        }
        }
        finally { IsRestoring = false; Changed?.Invoke(); }
    }
    internal async Task StopAsync()
    {
        if (session.Profile.IsLayer) { AcceptRemoteSnapshot([]); return; }
        await changes.WaitAsync();
        try
        {
            Running[] plugins; lock (active) { plugins = active.Values.ToArray(); active.Clear(); }
            foreach (var plugin in plugins) try { await plugin.DisposeAsync(); } catch (Exception error) { session.Log("Plugin", error.Message); }
            if (procon is not null) { await procon.DisposeAsync(); procon = null; }
            libraryStamp = null;
            libraryPaths.Clear();
            lock (active) proconPaths.Clear();
            lock (details)
            {
                cachedSettings.Clear();
                foreach (var path in details.Keys.ToArray()) details[path] = details[path] with { Enabled = false };
            }
        }
        finally { changes.Release(); Changed?.Invoke(); }
    }
    internal void Dispatch(string[] words)
    {
        lock (active) foreach (var plugin in active) plugin.Value.Dispatch(words, error => RecordError(plugin.Key, error));
        var runtime = procon;
        try { runtime?.Handle(words); }
        catch (Exception error)
        {
            var stopped = runtime is not { IsAlive: true };
            if (stopped && ReferenceEquals(Interlocked.Exchange(ref failedProcon, runtime), runtime)) return;
            string[] paths; lock (active) paths = proconPaths.ToArray();
            foreach (var path in paths)
            {
                if (stopped) lock (details) details[path] = details[path] with { Enabled = false };
                RecordError(path, error);
            }
            if (stopped) Changed?.Invoke();
        }
    }
    internal async Task<System.Text.Json.JsonElement> ProconCommand(string[] words)
    {
        if (procon is null) throw new InvalidOperationException(UiText.Get("Load a Procon plugin before using Procon services."));
        return await procon.Request("command", words: words);
    }
    internal async Task<System.Text.Json.JsonElement> ProconCall(string name, string method, object[] arguments)
    {
        if (procon is null) throw new InvalidOperationException(UiText.Get("Load a Procon plugin before using Procon services."));
        await procon.SyncAccountsAsync();
        return await procon.Request("call", name, method: method, arguments: arguments);
    }
    internal async Task RefreshAccountsAsync()
    {
        await changes.WaitAsync(session.LifetimeToken);
        try { if (procon is not null) await procon.SyncAccountsAsync(); }
        finally { changes.Release(); }
    }
}
