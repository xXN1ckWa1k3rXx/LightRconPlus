using LightRcon.Core;

namespace LightRcon.Host;

internal static class HostConsole
{
    public static async Task ReadKeysAsync(IReadOnlyList<ServerSession> sessions, Func<ConsoleKey?> readKey, Action<string> write, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (readKey() == ConsoleKey.F3)
                {
                    write("F3: reconnecting all running server profiles.");
                    await Task.WhenAll(sessions.Select(async session =>
                    {
                        try
                        {
                            if (session.State != ConnectionState.RetryWaiting) await session.StopAsync();
                            if (!token.IsCancellationRequested) await session.StartAsync();
                        }
                        catch (Exception error) { write($"[{session.Profile.Name}] [{session.Profile.Id}] Manual reconnect failed: {error.Message}"); }
                    }));
                }
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public static ConsoleKey? ReadKey()
    {
        try { return !Console.IsInputRedirected && Console.KeyAvailable ? Console.ReadKey(true).Key : null; }
        catch (Exception error) when (error is InvalidOperationException or IOException) { return null; }
    }
}
