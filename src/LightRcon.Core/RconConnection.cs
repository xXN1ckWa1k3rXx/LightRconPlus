using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace LightRcon.Core;

public interface ICommandConnection : IAsyncDisposable
{
    event Action<string[]>? EventReceived;
    event Action<ActivityEntry[]>? HistoryReceived;
    Task Completion { get; }
    Task<string[]> ExecuteAsync(string[] words, CancellationToken token = default);
}

public sealed class RconException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class RconConnection : ICommandConnection
{
    private readonly TcpClient client = new() { NoDelay = true };
    private readonly SemaphoreSlim writeGate = new(1);
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<string[]>> pending = new();
    private readonly CancellationTokenSource lifetime = new();
    private NetworkStream? stream;
    private int sequence;
    public event Action<string[]>? EventReceived;
    // Direct RCON never has replayed history.
    public event Action<ActivityEntry[]>? HistoryReceived { add { } remove { } }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public GameId Game { get; private set; } = GameId.BF4;

    public async Task ConnectAsync(string host, int port, string password, CancellationToken token, bool subscribeEvents = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await client.ConnectAsync(host, port, timeout.Token);
        stream = client.GetStream();
        Completion = ReadLoopAsync();
        var version = await ExecuteAsync(["version"], token);
        if (version.Length < 2) throw new RconException("Missing game identifier in version response.");
        Game = GameCatalog.Parse(version[1]);
        var salt = await ExecuteAsync(["login.hashed"], token);
        if (salt.Length < 2) throw new InvalidDataException("Missing login salt.");
        var data = Convert.FromHexString(salt[1]).Concat(Encoding.UTF8.GetBytes(password)).ToArray();
        // MD5 is required by the Battlefield RCON protocol, not used for Layer accounts.
        var hash = Convert.ToHexString(MD5.HashData(data));
        CryptographicOperations.ZeroMemory(data);
        await ExecuteAsync(["login.hashed", hash], token);
        if (subscribeEvents) await ExecuteAsync(["admin.eventsEnabled", "true"], token);
    }
    public async Task<string[]> ExecuteAsync(string[] words, CancellationToken token = default)
    {
        if (stream is null || lifetime.IsCancellationRequested) throw new IOException(UiText.Get("Connection closed."));
        var id = (uint)Interlocked.Increment(ref sequence) & 0x3fff_ffff;
        var response = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, response)) throw new IOException("Sequence exhausted.");
        try
        {
            await WriteAsync(new(id, false, false, words), token);
            var result = await response.Task.WaitAsync(TimeSpan.FromSeconds(12), token);
            if (result.Length == 0 || result[0] != "OK") throw new RconException(result.FirstOrDefault() ?? "EmptyResponse");
            return result;
        }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task WriteAsync(FrostbitePacket packet, CancellationToken token)
    {
        var bytes = packet.Encode();
        await writeGate.WaitAsync(token);
        try { await stream!.WriteAsync(bytes, token); }
        finally { writeGate.Release(); }
    }
    private async Task ReadLoopAsync()
    {
        Exception failure = new IOException(UiText.Get("The remote server closed the connection."));
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var packet = await FrostbitePacket.ReadAsync(stream!, lifetime.Token);
                if (packet.IsResponse && !packet.OriginServer)
                {
                    if (pending.TryRemove(packet.Sequence, out var response)) response.TrySetResult(packet.Words);
                }
                else if (!packet.IsResponse && packet.OriginServer)
                {
                    await WriteAsync(new(packet.Sequence, true, true, ["OK"]), lifetime.Token);
                    EventReceived?.Invoke(packet.Words);
                }
                else throw new InvalidDataException("Unexpected RCON packet origin.");
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or InvalidDataException or DecoderFallbackException)
        { failure = ex; }
        finally
        {
            lifetime.Cancel();
            foreach (var entry in pending.Values) entry.TrySetException(failure);
            client.Close();
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        client.Dispose();
        await Completion;
    }
}
