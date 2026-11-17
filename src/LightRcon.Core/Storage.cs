using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LightRcon.Core;

public sealed class AppState
{
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "system";
    public List<ServerProfile> Servers { get; set; } = [];
    public Guid? SelectedServer { get; set; }
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
}

public sealed class ProfileStore
{
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object gate = new();
    public string Root { get; }
    public AppState State { get; }
    public ProfileStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightRconPlus");
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "profiles.json");
        State = File.Exists(path) ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Json) ?? new() : new();
    }
    public void Save()
    {
        lock (gate)
        {
            List<ServerProfile> copies = [];
            foreach (var server in State.Servers.ToArray())
                lock (server.Sync)
                    copies.Add(JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(server, Json), Json)!);
            var snapshot = new AppState { Language = State.Language, Theme = State.Theme, Servers = copies, SelectedServer = State.SelectedServer, WindowWidth = State.WindowWidth, WindowHeight = State.WindowHeight };
            AtomicWrite(Path.Combine(Root, "profiles.json"), JsonSerializer.Serialize(snapshot, Json));
        }
    }
    public string ServerDirectory(Guid id)
    {
        var path = Path.Combine(Root, "servers", id.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    public static void AtomicWrite(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class SecretProtection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
    private static byte[] Transform(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credentials use Windows DPAPI.");
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var ok = protect ? CryptProtectData(ref input, "LightRcon+", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); CryptographicOperations.ZeroMemory(value); }
    }
}

public static class PasswordHash
{
    private const int Iterations = 210_000;
    public static string Create(string password)
    {
        if (password.Length is < 12 or > 1024) throw new ArgumentException(UiText.Get("Layer passwords must contain 12–1024 characters."));
        var salt = RandomNumberGenerator.GetBytes(24);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split(':');
            var iterations = int.Parse(parts[0]);
            if (iterations < 100_000 || iterations > 1_000_000) return false;
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(parts[2]));
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException) { return false; }
    }
}
