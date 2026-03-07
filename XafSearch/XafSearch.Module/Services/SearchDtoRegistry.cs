using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;

namespace XafSearch.Module.Services;

public class SearchDtoRegistry
{
    private static readonly Lazy<SearchDtoRegistry> _instance = new(() => new SearchDtoRegistry());
    public static SearchDtoRegistry Instance => _instance.Value;

    private readonly Dictionary<int, RegistryEntry> _entries = new();
    private readonly SearchDtoCompiler _compiler = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _entityTypeIndex = new(StringComparer.OrdinalIgnoreCase);

    private SearchDtoRegistry() { }

    public void Bootstrap(IObjectSpace objectSpace, ModuleBase module)
    {
        var configs = objectSpace.GetObjectsQuery<SearchConfiguration>()
            .Where(c => c.IsActive)
            .ToList();

        foreach (var config in configs)
        {
            CompileAndRegister(config, module);
        }

        Tracing.Tracer.LogText($"SearchDtoRegistry bootstrapped: {_entries.Count} search panel(s) registered.");
    }

    public CompilationResult CompileAndRegister(SearchConfiguration config, ModuleBase module)
    {
        var result = _compiler.Compile(config);
        if (!result.Success)
        {
            Tracing.Tracer.LogError($"Search DTO compilation failed for '{config.Name}': {string.Join("; ", result.Errors)}");
            return result;
        }

        lock (_lock)
        {
            if (_entries.TryGetValue(config.ID, out var oldEntry))
            {
                module.AdditionalExportedTypes.Remove(oldEntry.DtoType);
                _entityTypeIndex.Remove(oldEntry.TargetEntityType);
            }

            var entry = new RegistryEntry
            {
                ConfigurationId = config.ID,
                DtoType = result.DtoType,
                Source = result.Source,
                TargetEntityType = config.TargetEntityType
            };

            _entries[config.ID] = entry;
            _entityTypeIndex[config.TargetEntityType] = config.ID;

            XafTypesInfo.Instance.RegisterEntity(result.DtoType);
            module.AdditionalExportedTypes.Add(result.DtoType);
        }

        Tracing.Tracer.LogText($"Search DTO registered: {result.DtoType.FullName} for {config.TargetEntityType}");
        return result;
    }

    public void Unregister(int configId, ModuleBase module)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(configId, out var entry))
            {
                module.AdditionalExportedTypes.Remove(entry.DtoType);
                _entityTypeIndex.Remove(entry.TargetEntityType);
                _entries.Remove(configId);
            }
        }
    }

    public Type GetDtoType(string targetEntityTypeName)
    {
        lock (_lock)
        {
            if (_entityTypeIndex.TryGetValue(targetEntityTypeName, out var configId)
                && _entries.TryGetValue(configId, out var entry))
            {
                return entry.DtoType;
            }
            return null;
        }
    }

    public string GetSource(int configId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(configId, out var entry) ? entry.Source : null;
        }
    }

    public string GetExportSource(SearchConfiguration config)
    {
        return _compiler.GenerateExportSource(config);
    }

    public bool HasSearchPanel(string targetEntityTypeName)
    {
        lock (_lock)
        {
            return _entityTypeIndex.ContainsKey(targetEntityTypeName);
        }
    }

    public IReadOnlyList<RegistryEntry> GetAll()
    {
        lock (_lock)
        {
            return _entries.Values.ToList();
        }
    }
}

public class RegistryEntry
{
    public int ConfigurationId { get; set; }
    public Type DtoType { get; set; }
    public string Source { get; set; }
    public string TargetEntityType { get; set; }
}
