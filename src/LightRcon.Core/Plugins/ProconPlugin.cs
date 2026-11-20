using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LightRcon.Core.Plugins;

internal sealed class ProconPlugin : IAsyncDisposable
{
    private const string Digest = "3B3F59F3DC70EA3DB96091B17E5ACF352094B60F9939ED37784E08B589442A49";
    private const string Package = "Procon-v1.5.4.2.zip";
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource lifetime;
    private readonly ServerSession session;
    private readonly string root;
    private readonly Channel<FrostbitePacket> outgoing = Channel.CreateBounded<FrostbitePacket>(2048);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> requests = new();
    private readonly SemaphoreSlim inputGate = new(1);
    private readonly SemaphoreSlim accountsGate = new(1);
    private readonly Channel<string[]> events = Channel.CreateBounded<string[]>(2048);
    private Task? eventPump;
    private string accountSnapshot = "";
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? process;
    private Task? bridge;
    private int sequence;
    private int requestId;
    private volatile bool subscribed;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private sealed record CacheInput(string SourceHash, string ExternalDllHash, bool Precompiled);
    public bool IsAlive => process is { HasExited: false } && bridge is { IsCompleted: false } && !lifetime.IsCancellationRequested;
    public Dictionary<string, Exception> StagingErrors { get; } = new(StringComparer.OrdinalIgnoreCase);
    private ProconPlugin(ServerSession session, string root)
    {
        this.session = session; this.root = root;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.LifetimeToken);
    }
    public static async Task<ProconPlugin> Start(string[] paths, ProfileStore store, ServerSession session)
    {
        var plugin = new ProconPlugin(session, Path.Combine(store.ServerDirectory(session.Profile.Id), "plugins", "procon"));
        try { await plugin.StartAsync(paths, store.Root); return plugin; }
        catch { await plugin.DisposeAsync(); throw; }
    }
    private async Task StartAsync(string[] paths, string cache)
    {
        var zip = Path.Combine(cache, Package);
        if (!File.Exists(zip))
        {
            session.Log("Plugin", UiText.Get("Downloading Procon compatibility runtime…"));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
            var bytes = await http.GetByteArrayAsync("https://github.com/AdKats/Procon-1/releases/download/v1.5.4.2/" + Package, lifetime.Token);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != Digest) throw new InvalidDataException(UiText.Get("Procon archive checksum mismatch."));
            var temporary = zip + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporary, bytes, lifetime.Token);
            File.Move(temporary, zip, true);
        }
        using var file = File.OpenRead(zip);
        if (Convert.ToHexString(await SHA256.HashDataAsync(file)) != Digest) throw new InvalidDataException(UiText.Get("Procon archive checksum mismatch."));
        file.Position = 0;
        using var archive = new ZipArchive(file);
        Directory.CreateDirectory(root);
        accountSnapshot = JsonSerializer.Serialize(ProconAccounts.Snapshot(session));
        File.WriteAllText(Path.Combine(root, "LightRcon.Accounts.json"), accountSnapshot);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0 && !entry.FullName.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase)))
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid archive path.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination)) entry.ExtractToFile(destination);
        }
        var folder = Directory.CreateDirectory(Path.Combine(root, "Plugins", session.Game.ToString())).FullName;
        var cacheInputsPath = Path.Combine(folder, "LightRcon.CacheInputs.json");
        var cacheInputs = File.Exists(cacheInputsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, CacheInput>>(File.ReadAllText(cacheInputsPath), Json) ?? [] : [];
        var precompiled = new List<string>();
        var invalidate = new List<string>();
        var pluginNames = paths.Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        const string stub = "using PRoCon.Core; using PRoCon.Core.Players; using PRoCon.Core.Plugin;";
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!SyntaxFacts.IsValidIdentifier(name)) throw new InvalidDataException(UiText.Get("Procon filenames must match the plugin class name."));
            var binary = Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase);
            var suppliedDll = Path.ChangeExtension(path, ".dll");
            var suppliedHash = File.Exists(suppliedDll) && IsManagedAssembly(suppliedDll)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(suppliedDll))) : "";
            var sourceHash = binary ? "" : SourceHash(path);
            cacheInputs.TryGetValue(name, out var previous);
            var usePrecompiled = binary || suppliedHash.Length > 0 && (previous is null
                || previous.ExternalDllHash != suppliedHash || previous.Precompiled && previous.SourceHash == sourceHash);
            try { Stage(path, folder, pluginNames, usePrecompiled); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                StagingErrors[path] = error;
                var failedSource = Path.Combine(folder, name + ".cs");
                if (File.Exists(failedSource)) File.Delete(failedSource);
                continue;
            }
            var targetDll = Path.Combine(folder, name + ".dll");
            if (usePrecompiled)
            {
                File.Copy(suppliedDll, targetDll, true);
                var source = Path.Combine(folder, name + ".cs");
                File.WriteAllText(source, stub);
                precompiled.Add(name);
            }
            else if (previous is { Precompiled: true } || previous is not null && previous.SourceHash != sourceHash
                || File.Exists(targetDll) && !IsManagedAssembly(targetDll)) invalidate.Add(name);
            cacheInputs[name] = new(sourceHash, suppliedHash, usePrecompiled);
        }
        File.WriteAllText(cacheInputsPath, JsonSerializer.Serialize(cacheInputs, Json));
        File.WriteAllText(Path.Combine(folder, "LightRcon.CachePolicy.json"), JsonSerializer.Serialize(new { precompiled, invalidate }));
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var configs = Path.Combine(root, "Configs");
        File.WriteAllLines(Path.Combine(configs, "procon.cfg"), ["procon.private.options.pluginLogging True", "procon.private.options.autoCheckDownloadUpdates False", $"procon.private.servers.add 127.0.0.1 {port} {secret}"]);
        var start = new ProcessStartInfo(BuildWorker()) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add(session.Profile.Host);
        start.ArgumentList.Add(session.Profile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Exited += (_, _) => Fail(new IOException(UiText.Get("Procon compatibility runtime stopped.")));
        process.ErrorDataReceived += (_, args) => { if (args.Data is { } message) session.Log("Procon", message); };
        process.OutputDataReceived += (_, args) => Receive(args.Data);
        process.Start(); process.BeginErrorReadLine(); process.BeginOutputReadLine();
        bridge = ServeAsync();
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(50), lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
        catch (Exception error) { throw new InvalidDataException(UiText.Get("Procon compatibility runtime could not start.") + Environment.NewLine + Logs(), error); }
        eventPump = PumpEventsAsync();
    }
    private string SourceHash(string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sourceBase = Path.GetDirectoryName(path)!;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string file, int depth)
        {
            if (depth > 32 || !visited.Add(file)) return;
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(sourceBase, file)));
            if (!File.Exists(file)) { hash.AppendData(Encoding.UTF8.GetBytes("<missing>")); return; }
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
            foreach (System.Text.RegularExpressions.Match match in CSharpIncludes.Directive.Matches(File.ReadAllText(file)))
                Add(CSharpIncludes.Resolve(file, match.Groups["file"].Value, sourceBase, session.Game), depth + 1);
        }
        Add(path, 0);
        foreach (var partial in Directory.EnumerateFiles(sourceBase, Path.GetFileNameWithoutExtension(path) + ".*.cs").Order(StringComparer.OrdinalIgnoreCase)) Add(partial, 0);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private void Stage(string path, string folder, HashSet<string> pluginNames, bool precompiled)
    {
        File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), true);
        var parent = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var sibling in Directory.EnumerateFiles(parent))
        {
            var filename = Path.GetFileName(sibling);
            var extension = Path.GetExtension(sibling).ToLowerInvariant();
            if (filename.Equals("PluginCache.xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (filename == Path.GetFileName(path) || filename == "PRoCon.Core.dll" || filename.StartsWith("LightRcon.", StringComparison.OrdinalIgnoreCase)) continue;
            // Never overwrite another plugin's newly compiled cache with an older library DLL.
            if (extension == ".dll" && pluginNames.Contains(Path.GetFileNameWithoutExtension(sibling))) continue;
            if (extension is ".inc" or ".dll" or ".json" or ".xml" or ".txt" or ".cfg" or ".ini" || extension == ".cs" && (filename.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) || IsManagedAssembly(sibling)))
                File.Copy(sibling, Path.Combine(folder, filename), true);
        }
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            var directoryName = Path.GetFileName(directory);
            if (directoryName is "bin" or "obj" or ".git" or "servers" or "artifacts") continue;
            if (directoryName.Equals(name, StringComparison.OrdinalIgnoreCase) || directoryName.Equals("Includes", StringComparison.OrdinalIgnoreCase) || directoryName.Equals("Shared", StringComparison.OrdinalIgnoreCase))
                CopyDirectory(directory, Path.Combine(folder, directoryName));
        }
        if (!precompiled && Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(Path.Combine(folder, Path.GetFileName(path)), CSharpIncludes.Expand(path, session.Game));
            foreach (var partial in Directory.EnumerateFiles(parent, name + ".*.cs"))
                if (!IsManagedAssembly(partial)) File.WriteAllText(Path.Combine(folder, Path.GetFileName(partial)), CSharpIncludes.Expand(partial, session.Game));
        }
    }
    private static bool IsManagedAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') return false;
        stream.Position = 0;
        try { using var image = new PEReader(stream); return image.HasMetadata; }
        catch (BadImageFormatException) { return false; }
    }
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) if (!File.Exists(Path.Combine(target, Path.GetFileName(file)))) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    private string Logs()
    {
        var logs = Path.Combine(root, "Logs");
        try
        {
            if (!Directory.Exists(logs)) return "";
            var lines = new List<string>();
            foreach (var path in Directory.EnumerateFiles(logs, "*_plugin.log", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).Take(2))
            {
                try
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(input);
                    var tail = new Queue<string>();
                    while (reader.ReadLine() is { } line)
                    {
                        tail.Enqueue(line);
                        if (tail.Count > 64) tail.Dequeue();
                    }
                    lines.AddRange(tail);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ""; }
    }
    private string BuildWorker()
    {
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319");
        using var reader = new StreamReader(typeof(ProconPlugin).Assembly.GetManifestResourceStream("ProconWorker.cs")!);
        var references = new[] { Path.Combine(framework, "mscorlib.dll"), Path.Combine(framework, "System.dll"), Path.Combine(framework, "System.Core.dll"), Path.Combine(root, "Newtonsoft.Json.dll"), Path.Combine(root, "PRoCon.Core.dll") };
        var build = CSharpCompilation.Create("LightRcon.ProconWorker", [CSharpSyntaxTree.ParseText(reader.ReadToEnd())], references.Select(path => MetadataReference.CreateFromFile(path)), new(OutputKind.ConsoleApplication));
        var path = Path.Combine(root, "LightRcon.ProconWorker.exe");
        using var output = File.Create(path);
        var result = build.Emit(output);
        if (!result.Success) throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics));
        File.Copy(Path.Combine(root, "PRoCon.Console.exe.config"), path + ".config", true);
        return path;
    }
    private void Fail(Exception error)
    {
        ready.TrySetException(error);
        foreach (var response in requests.Values) response.TrySetException(error);
    }
    private void Receive(string? line)
    {
        if (line is null || !line.StartsWith("LRP:", StringComparison.Ordinal)) return;
        try
        {
            using var document = JsonDocument.Parse(line[4..]);
            var message = document.RootElement;
            if (message.TryGetProperty("ready", out _)) ready.TrySetResult();
            else if (message.TryGetProperty("log", out var log)) session.Log("Procon", log.GetString() ?? "");
            else if (message.TryGetProperty("id", out var id) && requests.TryRemove(id.GetInt32(), out var response))
            {
                try
                {
                    if (message.TryGetProperty("error", out var error)) response.TrySetException(new InvalidDataException(error.GetString() + Environment.NewLine + Logs()));
                    else response.TrySetResult(message.GetProperty("result").Clone());
                }
                catch (Exception error) { response.TrySetException(error); }
            }
        }
        catch (Exception error) { Fail(error); }
    }
    public async Task<JsonElement> Request(string operation, string name = "", string variable = "", string value = "", string[]? words = null, string method = "", object[]? arguments = null)
    {
        var id = Interlocked.Increment(ref requestId);
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests[id] = response;
        try
        {
            await inputGate.WaitAsync(lifetime.Token);
            try
            {
                if (process is null || process.HasExited) throw new IOException(UiText.Get("Procon compatibility runtime stopped."));
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, operation, name, variable, value, words, method, arguments }));
                await process.StandardInput.FlushAsync();
            }
            finally { inputGate.Release(); }
            return await response.Task.WaitAsync(TimeSpan.FromSeconds(50), lifetime.Token);
        }
        finally { requests.TryRemove(id, out _); }
    }
    public async Task<PluginDetails> Details(string path)
    {
        var data = await Request("details", Path.GetFileNameWithoutExtension(path));
        string Field(string key) => data.GetProperty(key).GetString() ?? "";
        return new(path, Field("Name"), Field("Version"), Field("Author"), Field("Website"), Field("Description"), "Procon");
    }
    public async Task<PluginSetting[]> Settings(string path)
    {
        var data = await Request("settings", Path.GetFileNameWithoutExtension(path));
        return data.Deserialize<PluginSetting[]>(Json) ?? [];
    }
    public void Handle(string[] words)
    {
        if (process?.HasExited == true || bridge?.IsCompleted == true) throw new IOException(UiText.Get("Procon compatibility runtime stopped."));
        if (subscribed && !events.Writer.TryWrite((string[])words.Clone())) throw new IOException(UiText.Get("Plugin event queue is full."));
    }
    public async Task SyncAccountsAsync()
    {
        await accountsGate.WaitAsync(lifetime.Token);
        try
        {
            var snapshot = JsonSerializer.Serialize(ProconAccounts.Snapshot(session));
            if (snapshot == accountSnapshot) return;
            await Request("accounts", value: snapshot);
            accountSnapshot = snapshot;
        }
        finally { accountsGate.Release(); }
    }
    private async Task PumpEventsAsync()
    {
        try
        {
            await foreach (var words in events.Reader.ReadAllAsync(lifetime.Token))
            {
                await SyncAccountsAsync();
                await outgoing.Writer.WriteAsync(new((uint)Interlocked.Increment(ref sequence), true, false, words), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); session.Log("Procon", error.Message); }
    }
    private async Task ServeAsync()
    {
        var token = lifetime.Token;
        try
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            client.NoDelay = true;
            using var stream = client.GetStream();
            var writer = Task.Run(async () => { await foreach (var packet in outgoing.Reader.ReadAllAsync(token)) await stream.WriteAsync(packet.Encode(), token); }, token);
            try
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                var expected = Convert.ToHexString(MD5.HashData([.. salt, .. Encoding.ASCII.GetBytes(secret)]));
                var authenticated = false;
                while (!token.IsCancellationRequested)
                {
                    var packet = await FrostbitePacket.ReadAsync(stream, token);
                    if (packet.IsResponse || packet.Words.Length == 0) continue;
                    var words = packet.Words;
                    string[] response;
                    if (words.Length == 1 && words[0] is "version" or "serverInfo") response = await session.ExecuteAsync(words, token);
                    else if (words[0] == "login.hashed" && words.Length == 1) response = ["OK", Convert.ToHexString(salt)];
                    else if (words[0] == "login.hashed") { authenticated = words.Length == 2 && string.Equals(expected, words[1], StringComparison.OrdinalIgnoreCase); response = [authenticated ? "OK" : "InvalidPasswordHash"]; }
                    else if (words[0] == "procon.version") response = ["UnknownCommand"];
                    else if (!authenticated) response = ["LoginRequired"];
                    else if (words[0] == "admin.eventsEnabled") { subscribed = words.ElementAtOrDefault(1)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true; response = ["OK"]; }
                    else
                    {
                        try { response = await session.ExecutePluginAsync(words, token); }
                        catch (Exception error) when (!token.IsCancellationRequested) { response = [error is RconException rcon ? rcon.Code : "InvalidArguments"]; session.Log("Procon", error.Message); }
                    }
                    await outgoing.Writer.WriteAsync(new(packet.Sequence, false, true, response), token);
                }
            }
            finally { outgoing.Writer.TryComplete(); await writer; }
        }
        catch (Exception error) when (!token.IsCancellationRequested) { Fail(error); session.Log("Procon", error.Message); }
    }
    public async ValueTask DisposeAsync()
    {
        if (process is { HasExited: false })
        {
            process.StandardInput.Close();
            if (!lifetime.IsCancellationRequested)
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
        }
        lifetime.Cancel(); listener.Stop(); outgoing.Writer.TryComplete();
        events.Writer.TryComplete();
        if (eventPump is not null) await eventPump;
        if (process is not null) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); process.Dispose(); }
        if (bridge is not null) try { await bridge; } catch (OperationCanceledException) { }
        Fail(new ObjectDisposedException(nameof(ProconPlugin)));
        lifetime.Dispose();
    }
}
