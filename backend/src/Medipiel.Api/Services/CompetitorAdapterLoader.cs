using System.Reflection;
using System.Runtime.Loader;
using Medipiel.Competitors.Abstractions;

namespace Medipiel.Api.Services;

public sealed class CompetitorAdapterLoader : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CompetitorAdapterLoader> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly CompetitorAdapterRegistry _registry;

    public CompetitorAdapterLoader(
        IServiceProvider serviceProvider,
        ILogger<CompetitorAdapterLoader> logger,
        IConfiguration configuration,
        IHostEnvironment environment,
        CompetitorAdapterRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var pluginsPath = _configuration.GetValue<string>("Plugins:Path");
        if (string.IsNullOrWhiteSpace(pluginsPath))
        {
            pluginsPath = Path.Combine(_environment.ContentRootPath, "plugins");
        }

        pluginsPath = Path.GetFullPath(pluginsPath);

        if (!Directory.Exists(pluginsPath))
        {
            Directory.CreateDirectory(pluginsPath);
            _logger.LogInformation("Plugins folder created at {Path}", pluginsPath);
            return Task.CompletedTask;
        }

        var dlls = Directory.GetFiles(pluginsPath, "Medipiel.Competitors.*.dll", SearchOption.AllDirectories)
            // We publish each adapter into plugins/<AdapterName>/... and keep the root clean.
            // Ignoring root-level dlls avoids duplicate loads and dependency mismatches.
            .Where(path => !string.Equals(Path.GetDirectoryName(path), pluginsPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("Medipiel.Competitors.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("Medipiel.Competitors.Core.dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (dlls.Length == 0)
        {
            _logger.LogInformation("No competitor adapters found in {Path}", pluginsPath);
            return Task.CompletedTask;
        }

        foreach (var dll in dlls)
        {
            LoadFromAssembly(dll);
        }

        _logger.LogInformation("Loaded {Count} competitor adapters.", _registry.Adapters.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void LoadFromAssembly(string assemblyPath)
    {
        try
        {
            var loadContext = new PluginLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var adapterTypes = assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && typeof(ICompetitorAdapter).IsAssignableFrom(type))
                .ToList();

            if (adapterTypes.Count == 0)
            {
                return;
            }

            foreach (var type in adapterTypes)
            {
                var adapter = ActivatorUtilities.CreateInstance(_serviceProvider, type) as ICompetitorAdapter;
                if (adapter is null)
                {
                    continue;
                }

                if (_registry.TryAdd(adapter))
                {
                    _logger.LogInformation(
                        "Adapter loaded: {AdapterId} ({Name}) from {Assembly}",
                        adapter.AdapterId,
                        adapter.Name,
                        Path.GetFileName(assemblyPath)
                    );
                }
                else
                {
                    _logger.LogWarning(
                        "Adapter with id {AdapterId} already registered.",
                        adapter.AdapterId
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load adapter assembly {Assembly}", assemblyPath);
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private static readonly string[] SharedAssemblyPrefixes =
        {
            "Medipiel.Competitors.Abstractions",
            "Microsoft.Extensions."
        };

        public PluginLoadContext(string pluginPath)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (!string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                foreach (var prefix in SharedAssemblyPrefixes)
                {
                    if (assemblyName.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null)
            {
                return null;
            }

            return LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path is null)
            {
                return IntPtr.Zero;
            }

            return LoadUnmanagedDllFromPath(path);
        }
    }
}
