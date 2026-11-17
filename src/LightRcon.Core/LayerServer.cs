using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

namespace LightRcon.Core;

public sealed class LayerServer : IAsyncDisposable
{
    private sealed class Peer(TcpClient client)
    {
        public TcpClient Client { get; } = client;
        public string Username { get; set; } = "";
        public Channel<LayerFrame> Queue { get; } = Channel.CreateBounded<LayerFrame>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        public void Send(LayerFrame frame) { if (!Queue.Writer.TryWrite(frame)) Client.Close(); }
    }
    private readonly ServerSession session;
    private readonly ProfileStore store;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<int, Peer> peers = new();
    private readonly ConcurrentDictionary<int, Task> workers = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> failures = new();
    private readonly object publishGate = new();
    private TcpListener? listener;
    private Task? accepting;
    private X509Certificate2? certificate;
    private int peerId;
    public string Fingerprint { get; private set; } = "";
    public int ClientCount => peers.Count;
    public LayerServer(ServerSession session, ProfileStore store) { this.session = session; this.store = store; }
    public Task StartAsync()
    {
        var options = session.Profile.Layer;
        if (options.Port is < 1 or > 65535) throw new ArgumentException(UiText.Get("Layer port must be between 1 and 65535."));
        if (!IPAddress.TryParse(options.BindAddress, out var address)) throw new ArgumentException(UiText.Get("Layer listening address must be an IP address."));
        var path = Path.Combine(store.ServerDirectory(session.Profile.Id), "layer-certificate.dpapi");
        if (File.Exists(path)) certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(SecretProtection.Unprotect(File.ReadAllText(path))), null, X509KeyStorageFlags.DefaultKeySet);
        else
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=LightRconPlus-" + session.Profile.Id.ToString("N"), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
            var pfx = generated.Export(X509ContentType.Pfx);
            ProfileStore.AtomicWrite(path, SecretProtection.Protect(Convert.ToBase64String(pfx)));
            certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet);
            CryptographicOperations.ZeroMemory(pfx);
        }
        Fingerprint = LayerWire.Fingerprint(certificate);
        listener = new(address, options.Port);
        listener.Start(32);
        session.Journal.Appended += OnActivity;
        session.RemoteEvent += OnEvent;
        accepting = AcceptAsync();
        return Task.CompletedTask;
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(lifetime.Token);
                if (workers.Count >= 32) { client.Dispose(); continue; }
                client.NoDelay = true;
                var id = Interlocked.Increment(ref peerId);
                var task = ServeAsync(id, new Peer(client));
                workers[id] = task;
                _ = task.ContinueWith(_ => { workers.TryRemove(id, out var ignored); }, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private LayerPermission? PermissionsFor(string name)
    {
        lock (session.Profile.Sync)
        {
            var account = session.Profile.Layer.Accounts.FirstOrDefault(x => x.Enabled && string.Equals(x.Username, name, StringComparison.OrdinalIgnoreCase));
            return account is null ? null : account.Permissions ?? session.Profile.Layer.GlobalPermissions;
        }
    }
    private async Task ServeAsync(int id, Peer peer)
    {
        using var client = peer.Client;
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = tokenSource.Token;
        var ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
        if (failures.TryGetValue(ip, out var blocked) && blocked.Count >= 5 && blocked.Until > DateTimeOffset.Now) return;
        using var stream = new SslStream(client.GetStream(), false);
        using var sendGate = new SemaphoreSlim(1);
        Task? sending = null;
        try
        {
            using var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            loginTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, ClientCertificateRequired = false, EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 }, loginTimeout.Token);
            var login = await LayerWire.ReadAsync(stream, loginTimeout.Token);
            LayerAccount? account;
            lock (session.Profile.Sync) account = session.Profile.Layer.Accounts.FirstOrDefault(x => x.Enabled && string.Equals(x.Username, login.Username, StringComparison.OrdinalIgnoreCase));
            // Same PBKDF2 workload for unknown accounts, to avoid a fast username oracle.
            var valid = login.Username.Length <= 64 && login.Password.Length <= 1024 && PasswordHash.Verify(login.Password, account?.PasswordHash ?? DummyHash);
            login.Password = "";
            if (login.Type != "login" || login.Protocol != 1 || account is null || !valid || PermissionsFor(account.Username) is not { } permissions || !permissions.HasFlag(LayerPermission.View))
            {
                if (failures.Count > 4096) foreach (var key in failures.Where(x => x.Value.Until < DateTimeOffset.Now).Select(x => x.Key).Take(1024)) failures.TryRemove(key, out _);
                failures.AddOrUpdate(ip, (1, DateTimeOffset.Now.AddMinutes(1)), (_, old) => (old.Until < DateTimeOffset.Now ? 1 : old.Count + 1, DateTimeOffset.Now.AddMinutes(1)));
                await LayerWire.WriteAsync(stream, new() { Type = "welcome", Error = UiText.Get("Invalid username, password or login permission.") }, sendGate, loginTimeout.Token);
                return;
            }
            peer.Username = account.Username;
            await LayerWire.WriteAsync(stream, new() { Type = "welcome", Permissions = permissions }, sendGate, loginTimeout.Token);
            // Subscribe before replay. Duplicate sequence numbers are ignored by clients;
            // replay must complete before live frames are drained to retain ordering.
            lock (publishGate)
            {
                peers[id] = peer;
            }
            foreach (var chunk in session.Journal.Replay())
            {
                using var replayTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                replayTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                await LayerWire.WriteAsync(stream, new() { Type = "history", Entries = chunk }, sendGate, replayTimeout.Token);
            }
            sending = SendLoopAsync(peer, stream, sendGate, token);
            session.Log("Layer", UiText.Format("{0} signed in ({1})", peer.Username, ip));
            // Initial synchronization reads many settings in a short burst. Bound that burst
            // separately from the sustained request rate so normal clients can finish login.
            var lastRequest = System.Diagnostics.Stopwatch.GetTimestamp();
            var requestBudget = 128d;
            while (!token.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromMinutes(2));
                var frame = await LayerWire.ReadAsync(stream, idle.Token);
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                requestBudget = Math.Min(128d, requestBudget + System.Diagnostics.Stopwatch.GetElapsedTime(lastRequest, now).TotalSeconds * 60d);
                lastRequest = now;
                if (requestBudget < 1) throw new IOException("Layer request rate limit exceeded.");
                requestBudget--;
                var response = new LayerFrame { Type = "response", Id = frame.Id };
                try
                {
                    var current = PermissionsFor(peer.Username) ?? LayerPermission.None;
                    LayerAuthorization.Demand(current, LayerPermission.View);
                    if (frame.Type is "command" or "console")
                    {
                        if (frame.Type == "console") LayerAuthorization.Demand(current, LayerPermission.Console);
                        var required = LayerAuthorization.Required(frame.Words);
                        LayerAuthorization.Demand(current, required);
                        response.Words = await session.ExecuteAsync(frame.Words, token);
                        session.Log("Layer audit", $"{peer.Username}: {frame.Words[0]}");
                    }
                    else if (frame.Type == "layer.get")
                    {
                        LayerAuthorization.Demand(current, LayerPermission.LayerAdmin);
                        lock (session.Profile.Sync)
                        {
                            var options = session.Profile.Layer;
                            response.Layer = new(options.Enabled, options.BindAddress, options.Port, options.GlobalPermissions,
                                options.Accounts.Select(x => new PublicLayerAccount(x.Username, x.Enabled, x.Permissions)).ToArray());
                        }
                    }
                    else if (frame.Type is "layer.account.save" or "layer.account.delete" or "layer.global")
                    {
                        LayerAuthorization.Demand(current, LayerPermission.LayerAdmin);
                        UpdateAccount(frame);
                        response.Words = ["OK"];
                        session.Log("Layer audit", $"{peer.Username}: {frame.Type}");
                    }
                    else throw new UnauthorizedAccessException("Unsupported Layer operation.");
                }
                catch (Exception ex) when (ex is RconException or UnauthorizedAccessException or ArgumentException or IOException or TimeoutException)
                { response.Error = ex.Message; }
                peer.Send(response);
                if ((frame.Type is "layer.account.save" or "layer.account.delete" or "layer.global") && response.Error is null)
                    RevalidateClients();
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or System.Security.Authentication.AuthenticationException or System.Text.Json.JsonException or InvalidDataException)
        { if (!token.IsCancellationRequested && peer.Username.Length > 0) session.Log("Layer", $"{peer.Username}: {ex.Message}"); }
        finally
        {
            peers.TryRemove(id, out _);
            tokenSource.Cancel();
            peer.Queue.Writer.TryComplete();
            client.Close();
            if (sending is not null) try { await sending; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private static readonly string DummyHash = PasswordHash.Create("LightRcon-unknown-account-dummy");
    private async Task SendLoopAsync(Peer peer, Stream stream, SemaphoreSlim gate, CancellationToken token)
    {
        try
        {
            await foreach (var frame in peer.Queue.Reader.ReadAllAsync(token))
            {
                if (frame.Type == "disconnect") { peer.Client.Close(); return; }
                await LayerWire.WriteAsync(stream, frame, gate, token).WaitAsync(TimeSpan.FromSeconds(20), token);
            }
        }
        catch { peer.Client.Close(); throw; }
    }
    private void UpdateAccount(LayerFrame frame)
    {
        lock (session.Profile.Sync)
        {
            var options = session.Profile.Layer;
            if (frame.Type == "layer.global") options.GlobalPermissions = frame.Permissions & LayerPermission.All;
            else
            {
                if (string.IsNullOrWhiteSpace(frame.Username) || frame.Username.Length > 64) throw new ArgumentException(UiText.Get("Invalid Layer username."));
                var existing = options.Accounts.FirstOrDefault(x => string.Equals(x.Username, frame.Username, StringComparison.OrdinalIgnoreCase));
                if (frame.Type == "layer.account.delete") { if (existing is not null) options.Accounts.Remove(existing); }
                else
                {
                    if (options.Accounts.Count >= 100 && existing is null) throw new ArgumentException(UiText.Get("At most 100 Layer accounts are supported."));
                    var hash = string.IsNullOrEmpty(frame.Password) ? null : PasswordHash.Create(frame.Password);
                    if (existing is null && hash is null) throw new ArgumentException(UiText.Get("New accounts require a password."));
                    if (existing is null) { existing = new() { Username = frame.Username }; options.Accounts.Add(existing); }
                    if (hash is not null) existing.PasswordHash = hash;
                    if (string.IsNullOrEmpty(existing.PasswordHash)) throw new ArgumentException(UiText.Get("New accounts require a password."));
                    existing.Permissions = frame.Layer?.Accounts.FirstOrDefault()?.Permissions is { } mask ? mask & LayerPermission.All : null;
                    existing.Enabled = frame.Layer?.Accounts.FirstOrDefault()?.Enabled ?? true;
                }
            }
        }
        store.Save();
    }
    private void OnActivity(ActivityEntry[] entries)
    {
        lock (publishGate)
            foreach (var peer in peers.Values)
                if (PermissionsFor(peer.Username)?.HasFlag(LayerPermission.View) == true)
                    foreach (var chunk in entries.Chunk(100)) peer.Send(new() { Type = "history", Entries = chunk });
    }
    private void OnEvent(string[] words)
    {
        lock (publishGate)
            foreach (var peer in peers.Values)
                if (PermissionsFor(peer.Username)?.HasFlag(LayerPermission.View) == true) peer.Send(new() { Type = "event", Words = words });
    }
    public void RevalidateClients()
    {
        foreach (var peer in peers.Values) peer.Send(new() { Type = "disconnect" });
        // Permission changes invalidate authenticated sessions immediately; reconnect gets the new mask.
    }
    public async ValueTask DisposeAsync()
    {
        session.Journal.Appended -= OnActivity;
        session.RemoteEvent -= OnEvent;
        lifetime.Cancel(); listener?.Stop();
        foreach (var peer in peers.Values) peer.Client.Close();
        if (accepting is not null) await accepting;
        try { await Task.WhenAll(workers.Values); } catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException) { }
        certificate?.Dispose();
    }
}
