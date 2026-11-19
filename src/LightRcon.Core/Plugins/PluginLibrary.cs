namespace LightRcon.Core.Plugins;

public static class PluginLibrary
{
    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "plugins");
    public static string GameFolder(GameId game) => game == GameId.BFHL ? "BFH" : game.ToString();
    public static string[] Discover(string root, GameId game)
    {
        var directory = Path.Combine(root, GameFolder(game));
        if (!Directory.Exists(directory)) return [];
        bool Supported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".dll" or ".lua";
        var files = Directory.EnumerateFiles(directory).Where(Supported).ToList();
        foreach (var package in Directory.EnumerateDirectories(directory))
        {
            if (File.GetAttributes(package).HasFlag(FileAttributes.ReparsePoint)) continue;
            var name = Path.GetFileName(package);
            var main = new[] { ".cs", ".dll", ".lua" }.Select(extension => Path.Combine(package, name + extension)).FirstOrDefault(File.Exists);
            if (main is not null) files.Add(main);
        }
        return files.GroupBy(path => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)), StringComparer.OrdinalIgnoreCase).Select(group => group.OrderBy(path => Path.GetExtension(path) == ".cs" ? 0 : 1).First()).ToArray();
    }
    public static async Task<string> CopyAsync(string path, GameId game, string? root = null)
    {
        path = Path.GetFullPath(path);
        var directory = Path.Combine(Path.GetFullPath(root ?? DefaultRoot), GameFolder(game));
        var name = Path.GetFileNameWithoutExtension(path);
        var destination = Path.Combine(directory, name);
        var target = Path.Combine(destination, Path.GetFileName(path));
        if (path.Equals(target, StringComparison.OrdinalIgnoreCase)) return path;
        if (!File.Exists(path)) throw new FileNotFoundException(UiText.Get("Plugin file was not found."), path);
        if (Directory.Exists(destination)) throw new IOException(UiText.Get("A plugin package with this name already exists in the plugin folder."));
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.GetDirectoryName(path)!;
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException(UiText.Get("Select a plugin outside the destination folder."));
            var pending = new Stack<string>(); pending.Push(source);
            var excluded = new HashSet<string>(["bin", "obj", ".git", ".local", ".vs", "artifacts", "servers", "logs"], StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> PackageFiles()
            {
                var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
                while (pending.TryPop(out var folder))
                {
                    foreach (var child in Directory.EnumerateDirectories(folder, "*", options))
                        if (!excluded.Contains(Path.GetFileName(child))) pending.Push(child);
                    foreach (var file in Directory.EnumerateFiles(folder, "*", options)) yield return file;
                }
            }
            foreach (var file in PackageFiles())
            {
                var relative = Path.GetRelativePath(source, file);
                if (Path.GetFileName(file).StartsWith("profiles.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetExtension(file).ToLowerInvariant() is not (".cs" or ".dll" or ".lua" or ".inc" or ".json" or ".xml" or ".cfg" or ".txt" or ".ini")) continue;
                var copy = Path.Combine(staging, relative); Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                await using var input = File.OpenRead(file); await using var output = File.Create(copy);
                await input.CopyToAsync(output);
            }
            Directory.Move(staging, destination);
            return target;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
