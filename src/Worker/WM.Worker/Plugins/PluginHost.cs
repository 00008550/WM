using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using WM.Plugins.Abstractions;

namespace WM.Worker.Plugins;

/// <summary>
/// Loads plugins from a directory. Each plugin gets its own collectible
/// AssemblyLoadContext so a plugin can be updated/unloaded without restarting
/// the worker, and its dependencies cannot clash with the host's.
/// </summary>
public sealed class PluginHost(ILogger<PluginHost> logger)
{
    private readonly Dictionary<string, LoadedPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<LoadedPlugin> Plugins => _plugins.Values;

    public void LoadFromDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            logger.LogInformation("Plugin directory {Root} does not exist — no plugins loaded", root);
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                LoadPlugin(manifestPath);
            }
            catch (Exception ex)
            {
                // One bad plugin must never stop the others (or the worker) from starting.
                logger.LogError(ex, "Failed to load plugin from {Manifest}", manifestPath);
            }
        }
    }

    public IEnumerable<T> GetPlugins<T>() where T : class, IWmPlugin =>
        _plugins.Values.Select(p => p.Instance).OfType<T>();

    private void LoadPlugin(string manifestPath)
    {
        var directory = Path.GetDirectoryName(manifestPath)!;
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(manifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("plugin.json could not be parsed.");

        var assemblyName = manifest.EntryAssembly ?? $"{manifest.Id}.dll";
        var assemblyPath = Path.Combine(directory, assemblyName);
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Plugin assembly not found: {assemblyPath}");

        var context = new PluginLoadContext(assemblyPath);
        var assembly = context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(assemblyPath));

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IWmPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            ?? throw new InvalidOperationException($"No IWmPlugin implementation in {assemblyName}.");

        var instance = (IWmPlugin)Activator.CreateInstance(pluginType)!;
        _plugins[manifest.Id] = new LoadedPlugin(manifest, instance, context);
        logger.LogInformation("Loaded plugin {Id} v{Version} ({Type})", manifest.Id, manifest.Version, manifest.Type);
    }
}

public sealed record LoadedPlugin(PluginManifest Manifest, IWmPlugin Instance, AssemblyLoadContext Context);

internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Contracts must come from the host so type identity matches across the boundary.
        if (assemblyName.Name is "WM.Plugins.Abstractions" or "Microsoft.Extensions.Logging.Abstractions")
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
