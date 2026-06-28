using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using Microsoft.Extensions.Logging;
using TelemetryDash.Core.Interfaces;

namespace TelemetryDash.Infrastructure.Plugins;

public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private CompositionContainer? _container;

    [ImportMany(typeof(IDataSourcePlugin))]
    public IEnumerable<IDataSourcePlugin> Plugins { get; private set; } = Enumerable.Empty<IDataSourcePlugin>();

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    public void LoadPlugins(string pluginDirectory)
    {
        var catalog = new AggregateCatalog();

        // Always include the current assembly (built-in plugins)
        catalog.Catalogs.Add(new AssemblyCatalog(typeof(PluginLoader).Assembly));

        // Load external plugins from directory if it exists
        if (Directory.Exists(pluginDirectory))
        {
            catalog.Catalogs.Add(new DirectoryCatalog(pluginDirectory));
            _logger.LogInformation("Loading external plugins from {Directory}", pluginDirectory);
        }

        _container = new CompositionContainer(catalog);

        try
        {
            _container.ComposeParts(this);
            _logger.LogInformation("Loaded {Count} plugin(s): {Names}",
                Plugins.Count(),
                string.Join(", ", Plugins.Select(p => p.Name)));
        }
        catch (CompositionException ex)
        {
            _logger.LogError(ex, "Failed to compose plugins");
        }
    }

    public IDataSourcePlugin? GetPlugin(string name)
    {
        return Plugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
