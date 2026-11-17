namespace LightRcon.Core;

/// <summary>Headless bootstrap options. Parse/validate before changing any saved profile.</summary>
public sealed class HostOptions
{
    public const string Help = """
        LightRcon.Host [--data-dir PATH] [--server GUID] [--list]
        LightRcon.Host --connect HOST:PORT --rcon-password-env VARIABLE
          [--name NAME] [--layer-bind ADDRESS] [--layer-port PORT]
          --admin-user USER --admin-password-env VARIABLE [--configure-only]

        --connect              Create or update a direct RCON profile and retry connections.
        --rcon-password        RCON password (visible in process arguments; prefer -env).
        --rcon-password-env    Read the RCON password from this environment variable.
        --admin-user           Bootstrap a Layer administrator with all permissions.
        --admin-password       Administrator password (prefer --admin-password-env).
        --admin-password-env   Read the administrator password from an environment variable.
        --layer-bind           Layer listener address (default: 127.0.0.1 for new profiles).
        --layer-port           Layer listener port (default: 47300).
        --configure-only       Save the profile and account without opening connections.
        --list                 List saved profiles without displaying secrets.
        --server               Run one saved profile; otherwise run all saved profiles.
        --help                 Show this help. Ctrl+C saves and stops running sessions.

        BF3 / BF4 / BFHL are detected automatically. Layer account management remains
        available from an authorized desktop Layer connection. Only one desktop/host
        process may use a given data directory. No desktop setup is required.
        """;
    private readonly Dictionary<string, string> values = [];
    public bool List { get; private set; }
    public bool ConfigureOnly { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? Get(string key) => values.GetValueOrDefault(key);
    public static HostOptions Parse(string[] args)
    {
        var options = new HostOptions();
        string[] valueKeys = ["--data-dir", "--server", "--connect", "--name", "--rcon-password", "--rcon-password-env", "--layer-bind", "--layer-port", "--admin-user", "--admin-password", "--admin-password-env"];
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--help") { options.ShowHelp = true; continue; }
            if (key == "--list") { options.List = true; continue; }
            if (key == "--configure-only") { options.ConfigureOnly = true; continue; }
            if (!valueKeys.Contains(key)) throw new ArgumentException("Unknown option: " + key);
            if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + key);
            if (!options.values.TryAdd(key, args[i])) throw new ArgumentException("Duplicate option: " + key);
        }
        if (options.ShowHelp) return options;
        if (options.Get("--server") is { } id && !Guid.TryParse(id, out _)) throw new ArgumentException("Invalid --server GUID.");
        if (options.Get("--connect") is { } address)
        {
            _ = ServerProfile.ParseEndpoint(address);
            if (options.Get("--server") is not null || options.List) throw new ArgumentException("--connect cannot be combined with --server or --list.");
            _ = options.Secret("--rcon-password");
            var user = options.Get("--admin-user");
            if (user is not null && (string.IsNullOrWhiteSpace(user) || user.Length > 64)) throw new ArgumentException("Invalid administrator username.");
            if (user is not null && options.Secret("--admin-password").Length < 12) throw new ArgumentException("Administrator password must contain at least 12 characters.");
            if (user is null && (options.Get("--admin-password") is not null || options.Get("--admin-password-env") is not null)) throw new ArgumentException("--admin-user is required.");
            if (options.Get("--layer-port") is { } port && (!int.TryParse(port, out var n) || n is < 1 or > 65535)) throw new ArgumentException("Invalid Layer port.");
            if (options.Get("--layer-bind") is { } bind && !System.Net.IPAddress.TryParse(bind, out _)) throw new ArgumentException("Layer bind address must be an IP address.");
        }
        else if (options.ConfigureOnly || options.values.Keys.Any(x => x is not ("--data-dir" or "--server"))) throw new ArgumentException("Bootstrap options require --connect.");
        return options;
    }
    private string Secret(string key)
    {
        var direct = Get(key); var variable = Get(key + "-env");
        if (direct is not null && variable is not null) throw new ArgumentException("Use either " + key + " or " + key + "-env.");
        var secret = variable is not null ? Environment.GetEnvironmentVariable(variable) : direct;
        if (string.IsNullOrEmpty(secret)) throw new ArgumentException("Provide " + key + " or a nonempty " + key + "-env variable.");
        return secret;
    }
    public ServerProfile Bootstrap(ProfileStore store)
    {
        var endpoint = ServerProfile.ParseEndpoint(Get("--connect") ?? throw new ArgumentException("--connect is required."));
        var rconPassword = Secret("--rcon-password");
        var username = Get("--admin-user");
        var adminHash = username is null ? null : PasswordHash.Create(Secret("--admin-password"));
        var profile = store.State.Servers.FirstOrDefault(x => !x.IsLayer && x.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase) && x.Port == endpoint.Port);
        if (profile is null) { profile = new() { Host = endpoint.Host, Port = endpoint.Port }; store.State.Servers.Add(profile); profile.Layer.BindAddress = "127.0.0.1"; }
        if (Get("--name") is { } name) profile.Name = name;
        profile.ProtectedPassword = SecretProtection.Protect(rconPassword); profile.AutoLogin = true;
        if (Get("--layer-bind") is { } bind) profile.Layer.BindAddress = bind;
        if (Get("--layer-port") is { } port) profile.Layer.Port = int.Parse(port, System.Globalization.CultureInfo.InvariantCulture);
        if (username is not null)
        {
            var account = profile.Layer.Accounts.FirstOrDefault(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (account is null) { account = new() { Username = username }; profile.Layer.Accounts.Add(account); }
            account.PasswordHash = adminHash!; account.Permissions = LayerPermission.All; account.Enabled = true;
            profile.Layer.Enabled = true;
        }
        store.Save(); return profile;
    }
}
