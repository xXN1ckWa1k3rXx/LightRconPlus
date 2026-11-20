using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LightRcon.Core;
using LightRcon.Host;

namespace LightRcon.Tests;

internal static class HostConnectionChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        async Task Wait(Func<bool> predicate, int seconds = 12)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            while (!predicate()) await Task.Delay(20, timeout.Token);
        }
        var store = new ProfileStore(Path.Combine(root, "host-connections"));
        await using var mock = new MockFrostbiteServer();
        var profile = new ServerProfile { Name = "Same server name", Host = "127.0.0.1", Port = mock.Port, AutoLogin = true, ProtectedPassword = SecretProtection.Protect("wrong") };
        store.State.Servers.Add(profile);
        await using var session = new ServerSession(profile, store, TimeSpan.FromSeconds(5));
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        var reporter = new HostStatusReporter(session, output.Enqueue, errors.Enqueue);
        session.Changed += reporter.Report;
        await session.StartAsync();
        await Wait(() => session.State == ConnectionState.RetryWaiting);
        check(session.RetryAt is { } retry && (retry - DateTimeOffset.Now).TotalSeconds is > 4 and <= 5 && session.Status.Contains("5"), "Host schedules automatic retries five seconds after failure");
        check(errors.Any(line => line.Contains("InvalidPasswordHash") && line.Contains("rejected the password") && line.Contains(profile.Id.ToString()) && line.Contains($"127.0.0.1:{mock.Port}") && line.Contains("Authenticating with RCON")), "Host reports rejected passwords with instance ID, endpoint and failure stage");
        await Wait(() => mock.LoginAttempts.Count >= 2);
        var attempts = mock.LoginAttempts.ToArray();
        check((attempts[1] - attempts[0]).TotalSeconds is >= 4.8 and < 8, "Host actually retries authentication after five seconds");
        await Wait(() => session.State == ConnectionState.RetryWaiting);
        var count = mock.LoginAttempts.Count;
        var keys = new ConcurrentQueue<ConsoleKey>();
        keys.Enqueue(ConsoleKey.F2);
        using var keyStop = new CancellationTokenSource();
        var keyboard = HostConsole.ReadKeysAsync([session], () => keys.TryDequeue(out var key) ? key : null, output.Enqueue, keyStop.Token);
        await Task.Delay(200);
        check(mock.LoginAttempts.Count == count, "Host ignores unrelated function keys");
        var watch = Stopwatch.StartNew();
        keys.Enqueue(ConsoleKey.F3);
        await Wait(() => mock.LoginAttempts.Count > count, 3);
        check(watch.Elapsed < TimeSpan.FromSeconds(3), "F3 interrupts the Host retry wait immediately");
        await Wait(() => session.State == ConnectionState.RetryWaiting);
        profile.ProtectedPassword = SecretProtection.Protect(MockFrostbiteServer.Password);
        keys.Enqueue(ConsoleKey.F3);
        await Wait(() => session.IsConnected && !session.Plugins.IsRestoring);
        count = mock.LoginAttempts.Count;
        keys.Enqueue(ConsoleKey.F3);
        await Wait(() => mock.LoginAttempts.Count > count && session.IsConnected);
        check(output.Any(line => line.Contains("F3: reconnecting")), "F3 reconnects an already connected Host session");
        keyStop.Cancel();
        await keyboard;
        await session.StopAsync();

        mock.RejectServerInfo = true;
        await session.StartAsync();
        await Wait(() => session.State == ConnectionState.RetryWaiting);
        check(session.LastError.Contains("serverInfo: InvalidArguments") && session.LastFailureStage == "Comparing local settings…" && !session.Status.Contains("Login failed"), "Post-authentication synchronization failures retain their actual command and stage instead of claiming login failed");
        await session.StopAsync();
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        profile.Port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await session.StartAsync();
        await Wait(() => session.State == ConnectionState.RetryWaiting);
        check(session.LastError.Contains("SocketError=ConnectionRefused") && errors.Any(line => line.Contains("SocketError=ConnectionRefused")), "Host exposes connection-refused socket details rather than a generic login failure");
        await session.StopAsync();
    }
}
