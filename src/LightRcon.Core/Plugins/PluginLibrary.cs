using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        var files = Directory.EnumerateFiles(directory).Where(Supported).Where(IsEntrypoint).ToList();
        foreach (var package in Directory.EnumerateDirectories(directory))
        {
            if (File.GetAttributes(package).HasFlag(FileAttributes.ReparsePoint)) continue;
            var name = Path.GetFileName(package);
            var main = new[] { ".cs", ".dll", ".lua" }.Select(extension => Path.Combine(package, name + extension)).FirstOrDefault(path => File.Exists(path) && IsEntrypoint(path));
            if (main is not null) files.Add(main);
        }
        return files.GroupBy(path => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path)), StringComparer.OrdinalIgnoreCase).Select(group => group.OrderBy(path => Path.GetExtension(path) == ".cs" ? 0 : 1).First()).ToArray();
    }
    private static bool IsEntrypoint(string path)
    {
        if (Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase)) return true;
        using var input = File.OpenRead(path);
        if (input.ReadByte() == 'M' && input.ReadByte() == 'Z')
        {
            // Some Procon packages distribute binary dependencies with a .cs extension.
            if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)) return false;
            input.Position = 0;
            try
            {
                using var pe = new PEReader(input);
                if (!pe.HasMetadata) return false;
                var metadata = pe.GetMetadataReader();
                bool PluginType(EntityHandle handle, HashSet<EntityHandle> visited)
                {
                    if (handle.IsNil || !visited.Add(handle)) return false;
                    if (handle.Kind == HandleKind.TypeReference)
                    {
                        var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
                        return KnownType(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
                    }
                    if (handle.Kind != HandleKind.TypeDefinition) return false;
                    var definition = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                    return PluginType(definition.BaseType, visited) || definition.GetInterfaceImplementations()
                        .Any(item => PluginType(metadata.GetInterfaceImplementation(item).Interface, visited));
                }
                return metadata.TypeDefinitions.Any(handle =>
                {
                    var definition = metadata.GetTypeDefinition(handle);
                    return metadata.GetString(definition.Name) == Path.GetFileNameWithoutExtension(path)
                        && (definition.Attributes & System.Reflection.TypeAttributes.Abstract) == 0
                        && PluginType(handle, []);
                });
            }
            catch (BadImageFormatException) { return false; }
        }
        if (!Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        var original = File.ReadAllText(path);
        // Include-only wrappers must remain visible even if their package is incomplete.
        if (System.Text.RegularExpressions.Regex.IsMatch(original, "#include\\s+\"")) return true;
        var syntax = CSharpSyntaxTree.ParseText(original).GetCompilationUnitRoot();
        return syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(type =>
            type.Identifier.ValueText == Path.GetFileNameWithoutExtension(path)
            && type.BaseList is { } bases && bases.Types.Any(item => item.Type.ToString().Split('.').Last() is "PRoConPluginAPI" or "IPRoConPluginInterface" or "ILightRconPlugin"));
    }
    private static bool KnownType(string space, string name) =>
        space == "PRoCon.Core.Plugin" && name is "PRoConPluginAPI" or "IPRoConPluginInterface"
        || space == "LightRcon.Core.Plugins" && name == "ILightRconPlugin";

    internal static string Stamp(string root, GameId game)
    {
        var directory = Path.Combine(root, GameFolder(game));
        if (!Directory.Exists(directory)) return "";
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
        var shared = new[] { "Includes", "Shared" }.Select(name => Path.Combine(root, name)).Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", options));
        var files = Directory.EnumerateFiles(directory, "*", options).Concat(Directory.EnumerateFiles(root)).Concat(shared)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".dll" or ".lua" or ".inc" or ".txt" or ".cfg" or ".ini" or ".json" or ".xml")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => { var file = new FileInfo(path); return Path.GetRelativePath(directory, path) + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks; });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', files))));
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
            // Keep parent/shared includes inside the imported package so it remains portable.
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToArray())
            {
                if (Path.GetExtension(file).ToLowerInvariant() is not (".cs" or ".inc")) continue;
                var original = Path.Combine(source, Path.GetRelativePath(staging, file));
                using (var input = File.OpenRead(original))
                    if (input.ReadByte() == 'M' && input.ReadByte() == 'Z') continue;
                if (CSharpIncludes.Directive.IsMatch(File.ReadAllText(original)))
                {
                    try { CSharpIncludes.CopyIncludes(original, file, source, staging, game); }
                    catch (FileNotFoundException) when (File.Exists(Path.ChangeExtension(original, ".dll"))) { /* A supplied binary can load without its original includes. */ }
                }
            }
            Directory.Move(staging, destination);
            return target;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
