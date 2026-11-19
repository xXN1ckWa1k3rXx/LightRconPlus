using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;

namespace LightRcon.Core;

public sealed class LayerConnection : ICommandConnection
{
    private readonly TcpClient client = new() { NoDelay = true };
    private readonly SemaphoreSlim writeGate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<LayerFrame>> pending = new();
    private SslStream? stream;
    private long sequence;
    public LayerPermission Permissions { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action<string[]>? EventReceived;
    public event Action<ActivityEntry[]>? HistoryReceived;
    public event Action<Plugins.PluginDetails[]>? PluginsReceived;
    public async Task ConnectAsync(ServerProfile profile, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await client.ConnectAsync(profile.Host, profile.Port, timeout.Token);
        // Layer uses account authentication. TLS encrypts transport without a manual certificate-pin workflow.
        stream = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null);
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = profile.Host, EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 }, timeout.Token);
        await LayerWire.WriteAsync(stream, new() { Type = "login", Username = profile.Username, Password = SecretProtection.Unprotect(profile.ProtectedPassword) }, writeGate, timeout.Token);
        var welcome = await LayerWire.ReadAsync(stream, timeout.Token);
        if (welcome.Type != "welcome" || welcome.Protocol != 1 || welcome.Error is not null) throw new UnauthorizedAccessException(welcome.Error ?? "Layer login failed.");
        Permissions = welcome.Permissions;
        Completion = ReadLoopAsync();
    }
    private async Task ReadLoopAsync()
    {
        Exception failure = new IOException(UiText.Get("Layer connection closed."));
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var frame = await LayerWire.ReadAsync(stream!, lifetime.Token);
                if (frame.Type == "response") { if (pending.TryRemove(frame.Id, out var promise)) promise.TrySetResult(frame); }
                else if (frame.Type == "event") EventReceived?.Invoke(frame.Words);
                else if (frame.Type == "history") HistoryReceived?.Invoke(frame.Entries);
                else if (frame.Type == "plugins") PluginsReceived?.Invoke(frame.Plugins);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException) { failure = ex; }
        finally
        {
            lifetime.Cancel();
            foreach (var item in pending.Values) item.TrySetException(failure);
            client.Close();
        }
    }
    public async Task<LayerFrame> RequestAsync(LayerFrame frame, CancellationToken token = default)
    {
        frame.Id = Interlocked.Increment(ref sequence);
        var promise = new TaskCompletionSource<LayerFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (pending.Count >= 128) throw new IOException("Too many pending Layer requests.");
        pending[frame.Id] = promise;
        try
        {
            await LayerWire.WriteAsync(stream!, frame, writeGate, token);
            var result = await promise.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            if (result.Error is not null) throw new RconException(result.Error);
            return result;
        }
        finally { pending.TryRemove(frame.Id, out _); }
    }
    public async Task<string[]> ExecuteAsync(string[] words, CancellationToken token = default)
        => (await RequestAsync(new() { Type = "command", Words = words }, token)).Words;
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); client.Dispose();
        await Completion;
        stream?.Dispose();
    }
}
