using System.Net.Sockets;

namespace LightRcon.Core;

internal static class ConnectionFailure
{
    public static string Describe(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            var message = current switch
            {
                RconException { Code: "InvalidPasswordHash" } rcon => UiText.Get("RCON authentication failed: the server rejected the password (InvalidPasswordHash).") + (rcon.Command is null ? "" : " [" + rcon.Command + "]"),
                SocketException socket => $"{socket.Message} (SocketError={socket.SocketErrorCode}, NativeError={socket.NativeErrorCode})",
                OperationCanceledException => UiText.Get("Connection or authentication timed out."),
                TimeoutException => UiText.Get("The server did not respond before the connection or command timeout.") + " " + current.Message,
                _ => current.Message
            };
            if (!messages.Contains(message, StringComparer.Ordinal)) messages.Add(message);
        }
        return string.Join(" -> ", messages);
    }
}
