using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LightRcon.Core.Plugins;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LightRconPlus_onlyAttribute(int apiVersion) : Attribute
{
    public int ApiVersion { get; } = apiVersion;
}

public sealed class PluginContext
{
    private readonly ServerSession session;
    internal PluginContext(ServerSession session) => this.session = session;
    public Task<string[]> CommandAsync(params string[] words) => session.ExecutePluginAsync(words);
    public void Log(string text) => session.Log("Plugin", text);
}

public interface ILightRconPlugin : IDisposable
{
    void Enable(PluginContext context);
    void OnEvent(string[] words);
}

internal sealed class NativePlugin : IDisposable, IPluginRuntime
{
    private readonly AssemblyLoadContext loader = new(null, isCollectible: true);
    private ILightRconPlugin? instance;
    private readonly PluginContext context;
    private bool enabled;
    public PluginDetails Details { get; }
    public static bool IsNative(string path)
    {
        if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
            return root.AttributeLists.Where(list => list.Target?.Identifier.ValueText == "assembly")
                .SelectMany(list => list.Attributes).Any(attr => attr.Name.ToString().Split('.').Last() is "LightRconPlus_only" or "LightRconPlus_onlyAttribute");
        }
        using var input = File.OpenRead(path);
        using var reader = new PEReader(input);
        var metadata = reader.GetMetadataReader();
        return metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == "LightRcon.Core");
    }
    public NativePlugin(string path, ServerSession session)
    {
        context = new(session);
        loader.Resolving += (context, name) =>
        {
            if (name.Name == typeof(PluginContext).Assembly.GetName().Name) return typeof(PluginContext).Assembly;
            var dependency = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, name.Name + ".dll");
            return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
        };
        Details = new(path, Path.GetFileNameWithoutExtension(path), "", "", "", "", "C#");
        try
        {
            using var image = Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ? Compile(path, session.Game) : File.OpenRead(path);
            var assembly = loader.LoadFromStream(image);
            var declaration = assembly.GetCustomAttribute<LightRconPlus_onlyAttribute>();
            if (declaration?.ApiVersion != 1) throw new InvalidDataException("Expected [assembly: LightRconPlus_only(1)].");
            var type = assembly.GetExportedTypes().Single(type => !type.IsAbstract && typeof(ILightRconPlugin).IsAssignableFrom(type));
            instance = (ILightRconPlugin)Activator.CreateInstance(type)!;
            if (instance is IPluginConfiguration configuration) Details = configuration.Details with { Path = path, Kind = "C#" };
        }
        catch { Dispose(); throw; }
    }
    private static unsafe MetadataReference Reference(Assembly assembly)
    {
        if (!assembly.TryGetRawMetadata(out var data, out var size)) throw new InvalidDataException(assembly.FullName);
        return AssemblyMetadata.Create(ModuleMetadata.CreateFromMetadata((IntPtr)data, size)).GetReference();
    }
    private static Stream Compile(string path, GameId game)
    {
        var assemblies = new[] { typeof(object).Assembly, typeof(PluginContext).Assembly, Assembly.Load("System.Runtime"), typeof(Console).Assembly, typeof(Enumerable).Assembly, Assembly.Load("System.Collections"), Assembly.Load("System.Threading.Tasks") };
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path), [CSharpSyntaxTree.ParseText(CSharpIncludes.Expand(path, game), path: path)], assemblies.Distinct().Select(Reference), new(OutputKind.DynamicallyLinkedLibrary));
        var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) { output.Dispose(); throw new InvalidDataException(string.Join(Environment.NewLine, result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error))); }
        output.Position = 0;
        return output;
    }
    public void Handle(string[] words) => instance!.OnEvent(words);
    public IReadOnlyList<PluginSetting> GetSettings() => instance is IPluginConfiguration configuration ? configuration.GetSettings() : [];
    public void SetSetting(string name, string value)
    {
        if (instance is IPluginConfiguration configuration) configuration.SetSetting(name, value);
        else throw new InvalidOperationException(UiText.Get("This plugin has no settings."));
    }
    public void Enable() { if (!enabled) { instance!.Enable(context); enabled = true; } }
    public void Tick() { }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    public void Dispose() { try { instance?.Dispose(); } finally { instance = null; loader.Unload(); } }
}
