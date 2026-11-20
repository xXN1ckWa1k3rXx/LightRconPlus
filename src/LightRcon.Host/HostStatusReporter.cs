using LightRcon.Core;

namespace LightRcon.Host;

internal sealed class HostStatusReporter(ServerSession session, Action<string> write, Action<string> writeError)
{
    private readonly object gate = new();
    private ConnectionState previous = (ConnectionState)(-1);
    private string previousError = "";

    public void Report()
    {
        lock (gate)
        {
            var profile = session.Profile;
            var label = $"[{DateTime.Now:T}] [{profile.Name}] [{profile.Id}] [{profile.Host}:{profile.Port}]";
            var error = session.LastError;
            if (error != previousError)
            {
                previousError = error;
                if (error.Length > 0) writeError($"{label} Failure during {session.LastFailureStage}: {error}");
            }
            if (previous == session.State) return;
            previous = session.State;
            var retry = session.State == ConnectionState.RetryWaiting && session.RetryAt is { } at
                ? $" Next attempt in {Math.Max(0, Math.Ceiling((at - DateTimeOffset.Now).TotalSeconds))} seconds; press F3 to reconnect now." : "";
            write($"{label} {session.Status}{retry}");
        }
    }
}
