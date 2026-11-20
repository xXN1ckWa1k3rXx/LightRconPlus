using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LightRcon.Core.Plugins;

// #include is a PRoCon extension; expand it before handing source to a C# compiler.
internal static class CSharpIncludes
{
    internal static readonly Regex Directive = new("^[\\t ]*#include[\\t ]+\"(?<file>[^\"\\r\\n]+)\"[\\t ]*(?://[^\\r\\n]*)?\\r?$", RegexOptions.Multiline);

    internal static string Resolve(string source, string reference, string entryDirectory, GameId game)
    {
        reference = reference.Replace("%GameType%", game.ToString());
        var relative = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, reference));
        if (File.Exists(relative)) return relative;
        // Older PRoCon includes resolve all nested paths from the entrypoint directory.
        var legacy = Path.GetFullPath(Path.Combine(entryDirectory, reference));
        return File.Exists(legacy) ? legacy : relative;
    }

    internal static string Expand(string path, GameId game)
    {
        var entryDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Read(string source)
        {
            source = Path.GetFullPath(source);
            if (stack.Count >= 32 || !stack.Add(source)) throw new InvalidDataException("Cyclic or excessively nested #include: " + source);
            try
            {
                return Directive.Replace(File.ReadAllText(source), match =>
                    Read(Resolve(source, match.Groups["file"].Value, entryDirectory, game)));
            }
            finally { stack.Remove(source); }
        }
        return Read(path);
    }

    internal static void CopyIncludes(string source, string destination, string sourceDirectory, string destinationDirectory, GameId game)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Copy(string input, string output, int depth = 0)
        {
            if (depth >= 32) throw new InvalidDataException("Excessively nested #include: " + input);
            if (!visited.Add(input)) return;
            var text = Directive.Replace(File.ReadAllText(input), match =>
            {
                var included = Resolve(input, match.Groups["file"].Value, sourceDirectory, game);
                var relative = Path.GetRelativePath(sourceDirectory, included);
                var external = Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                var target = external
                    ? Path.Combine(destinationDirectory, "Includes", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(included.ToUpperInvariant())))[..16] + Path.GetExtension(included))
                    : Path.Combine(destinationDirectory, relative);
                Copy(included, target, depth + 1);
                return "#include \"" + Path.GetRelativePath(Path.GetDirectoryName(output)!, target).Replace('\\', '/') + "\"";
            });
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, text);
        }
        Copy(Path.GetFullPath(source), destination);
    }
}
