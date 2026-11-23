using System.Text.RegularExpressions;

namespace LightRcon.Core.Plugins;

internal static class PluginDiagnostics
{
    private static readonly Regex QuotedPath = new("(?<quote>['\"])(?<path>(?:[A-Za-z]:[\\\\/]|\\\\\\\\|/(?!/))[^'\"\\r\\n]+)\\k<quote>", RegexOptions.Compiled);
    private static readonly Regex WindowsPath = new(@"(?<![\w:/])(?:[A-Za-z]:[\\/]|\\\\)[^'""\r\n<>|;]+", RegexOptions.Compiled);
    private static readonly Regex BarePath = new(@"(?<![\w:/<>])/(?!/)[^\s'""<>|,;()]+", RegexOptions.Compiled);
    public static string Redact(string text)
    {
        static string FileLabel(string path)
        {
            var normalized = path.Replace('\\', '/').TrimEnd('/');
            return "<path>/" + normalized[(normalized.LastIndexOf('/') + 1)..];
        }
        text = QuotedPath.Replace(text, match => match.Groups["quote"].Value + FileLabel(match.Groups["path"].Value) + match.Groups["quote"].Value);
        text = WindowsPath.Replace(text, match => FileLabel(match.Value));
        return BarePath.Replace(text, match => FileLabel(match.Value));
    }
}

internal sealed class LuaDiagnosticException(string message) : Exception(message);
